"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { headers } from "next/headers";
import { audit, getCurrentUser } from "@/lib/auth";
import { getDb } from "@/lib/db";
import { hashPassword, isPasswordlessAccount, passwordIssues, verifyPassword } from "@/lib/password";
import { PLANS, PLAN_ORDER } from "@/lib/licensing";
import { clean, isEmail } from "@/lib/validate";
import { newId, nowIso } from "@/lib/util";
import type { PlanId } from "@/lib/types";

function isPlan(value: string): value is PlanId {
  return (PLAN_ORDER as string[]).includes(value);
}

export async function requestPlanChangeAction(formData: FormData): Promise<void> {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const plan = clean(formData.get("plan"), 20);
  if (!isPlan(plan) || plan === "enterprise") {
    redirect("/dashboard/billing?error=invalid_plan");
  }

  const request = newId("req");
  getDb()
    .prepare("INSERT INTO messages (id, name, email, subject, body, created_at) VALUES (?, ?, ?, ?, ?, ?)")
    .run(
      request,
      user.name,
      user.email,
      `Plan change request → ${PLANS[plan].name}`,
      `Request from ${user.email} (${user.id}) to move to the ${PLANS[plan].name} plan at $${PLANS[plan].priceMonthly}/month. Current plan: ${user.plan}.`,
      nowIso(),
    );

  audit(user.id, "billing.plan_request", { plan });
  revalidatePath("/dashboard/billing");
  redirect(`/dashboard/billing?requested=${plan}`);
}

export async function updateProfileAction(formData: FormData): Promise<void> {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const name = clean(formData.get("name"), 80);
  const company = clean(formData.get("company"), 120);
  const email = clean(formData.get("email"), 254).toLowerCase();

  if (name.length < 2 || !isEmail(email)) redirect("/dashboard/settings?error=invalid_profile");

  if (email !== user.email) {
    const taken = getDb().prepare("SELECT id FROM users WHERE email = ? AND id != ?").get(email, user.id);
    if (taken) redirect("/dashboard/settings?error=email_taken");
  }

  getDb()
    .prepare("UPDATE users SET name = ?, company = ?, email = ?, updated_at = ? WHERE id = ?")
    .run(name, company || null, email, nowIso(), user.id);

  audit(user.id, "user.profile_updated", { email });
  revalidatePath("/dashboard/settings");
  redirect("/dashboard/settings?saved=profile");
}

export async function changePasswordAction(formData: FormData): Promise<void> {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const current = String(formData.get("currentPassword") ?? "");
  const next = String(formData.get("newPassword") ?? "");

  // A license-first account has no password, so there is nothing to confirm.
  // Setting one here is how it graduates to a normal account. For an account
  // that already has a password the current one is still required.
  if (!isPasswordlessAccount(user.password_hash) && !verifyPassword(current, user.password_hash)) {
    redirect("/dashboard/settings?error=wrong_password");
  }

  const issues = passwordIssues(next);
  if (issues.length > 0) redirect("/dashboard/settings?error=weak_password");

  getDb()
    .prepare("UPDATE users SET password_hash = ?, updated_at = ? WHERE id = ?")
    .run(hashPassword(next), nowIso(), user.id);

  const h = await headers();
  audit(user.id, "user.password_changed", { ip: h.get("x-forwarded-for") });
  revalidatePath("/dashboard/settings");
  redirect("/dashboard/settings?saved=password");
}
