"use server";

import { headers } from "next/headers";
import { redirect } from "next/navigation";
import { audit, createSession, destroySession, getCurrentUser, roleForEmail } from "@/lib/auth";
import { getDb } from "@/lib/db";
import { hashPassword, passwordIssues, verifyPassword } from "@/lib/password";
import { clientKey, rateLimit } from "@/lib/rate-limit";
import { isEmail } from "@/lib/validate";
import { newId, nowIso } from "@/lib/util";
import { checkLicenseKey, claimSeat, normalizeKey } from "@/lib/licensing";
import type { User } from "@/lib/types";

export interface AuthFormState {
  error?: string;
  fieldErrors?: Record<string, string>;
}

async function requestMeta(): Promise<{ userAgent: string | null; ip: string | null }> {
  const h = await headers();
  const forwarded = h.get("x-forwarded-for");
  return {
    userAgent: h.get("user-agent"),
    ip: forwarded ? forwarded.split(",")[0].trim() : h.get("x-real-ip"),
  };
}

export async function registerAction(
  _prev: AuthFormState,
  formData: FormData,
): Promise<AuthFormState> {
  const name = String(formData.get("name") ?? "").trim();
  const email = String(formData.get("email") ?? "").trim().toLowerCase();
  const company = String(formData.get("company") ?? "").trim();
  const password = String(formData.get("password") ?? "");
  const licenseKey = normalizeKey(String(formData.get("licenseKey") ?? ""));

  const meta = await requestMeta();
  const limit = rateLimit(clientKey(["register", meta.ip]), 5, 15 * 60_000);
  if (!limit.ok) {
    return { error: "Too many attempts. Try again in a few minutes." };
  }

  const fieldErrors: Record<string, string> = {};
  if (name.length < 2) fieldErrors.name = "Enter your full name.";
  if (!isEmail(email)) fieldErrors.email = "Enter a valid work email address.";
  const issues = passwordIssues(password);
  if (issues.length > 0) fieldErrors.password = `Password ${issues.join(", ")}.`;

  // Registration is gated by licensing: an admin issues a key with a fixed number
  // of seats, and each new account consumes one. No key, an unknown key, or a key
  // with no free seats means the account cannot be created.
  if (!licenseKey) fieldErrors.licenseKey = "Enter the license key issued to you by your administrator.";
  if (Object.keys(fieldErrors).length > 0) return { fieldErrors };

  const check = checkLicenseKey(licenseKey);
  if (!check.valid || !check.license) {
    const reason =
      check.reason === "suspended"
        ? "That license key is suspended. Contact your administrator."
        : check.reason === "revoked"
          ? "That license key has been revoked."
          : check.reason === "expired"
            ? "That license key has expired. Contact your administrator."
            : "That license key was not recognised. Check it with your administrator.";
    return { fieldErrors: { licenseKey: reason } };
  }
  if ((check.seatsRemaining ?? 0) <= 0) {
    return {
      fieldErrors: {
        licenseKey: "That license key has no free seats left. Ask your administrator to add seats.",
      },
    };
  }

  const db = getDb();
  const existing = db.prepare("SELECT id FROM users WHERE email = ?").get(email);
  if (existing) return { fieldErrors: { email: "An account with that email already exists." } };

  const id = newId("usr");
  db.prepare(
    `INSERT INTO users (id, email, name, company, password_hash, role, plan, created_at, updated_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
  ).run(
    id,
    email,
    name,
    company || null,
    hashPassword(password),
    roleForEmail(email),
    check.license.plan,
    nowIso(),
    nowIso(),
  );

  // Take one seat for the new account, then bind the key to it if the admin
  // minted it unassigned. Both are best-effort under the seat check above.
  const claimed = claimSeat(check.license.id, email, "account", id);
  if (!claimed.ok) {
    // Seat filled up between the check and the claim (concurrent sign-up). Undo.
    db.prepare("DELETE FROM users WHERE id = ?").run(id);
    return {
      fieldErrors: {
        licenseKey: "That license key filled up just now. Ask your administrator to add seats.",
      },
    };
  }
  db.prepare("UPDATE licenses SET user_id = ? WHERE id = ? AND user_id IS NULL").run(id, check.license.id);

  await createSession(id, { remember: false, ...meta });
  audit(id, "user.registered", { email, license: check.license.id });

  redirect("/dashboard");
}

export async function loginAction(_prev: AuthFormState, formData: FormData): Promise<AuthFormState> {
  const email = String(formData.get("email") ?? "").trim().toLowerCase();
  const password = String(formData.get("password") ?? "");

  const meta = await requestMeta();
  const limit = rateLimit(clientKey(["login", meta.ip]), 10, 10 * 60_000);
  if (!limit.ok) return { error: "Too many sign-in attempts. Try again shortly." };

  if (!isEmail(email) || password.length === 0) {
    return { error: "Enter your email and password." };
  }

  const user = getDb().prepare("SELECT * FROM users WHERE email = ?").get(email) as User | undefined;

  if (!user || !verifyPassword(password, user.password_hash)) {
    audit(user?.id ?? null, "user.login_failed", { email });
    return { error: "Those credentials are not valid." };
  }

  await createSession(user.id, { remember: formData.get("remember") === "on", ...meta });
  audit(user.id, "user.login", { email });

  redirect("/dashboard");
}

export async function logoutAction(): Promise<void> {
  const user = await getCurrentUser();
  if (user) audit(user.id, "user.logout", { email: user.email });
  await destroySession();
  redirect("/");
}
