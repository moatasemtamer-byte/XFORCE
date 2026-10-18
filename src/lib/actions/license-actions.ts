"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { audit, getCurrentUser } from "@/lib/auth";
import { getDb } from "@/lib/db";
import {
  countActiveLicenses,
  createLicense,
  effectivePlan,
  issueLicenseForUser,
  listLicenses,
  PLANS,
  PLAN_ORDER,
  releaseSeat,
  setLicenseStatus,
  updateLicenseTerms,
} from "@/lib/licensing";
import { clean } from "@/lib/validate";
import type { LicenseStatus, PlanId } from "@/lib/types";

function isPlan(value: string): value is PlanId {
  return (PLAN_ORDER as string[]).includes(value);
}

function isStatus(value: string): value is LicenseStatus {
  return ["active", "suspended", "revoked", "expired"].includes(value);
}

export async function issueLicenseAction(formData: FormData): Promise<void> {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const planRaw = clean(formData.get("plan"), 20);
  const plan: PlanId = isPlan(planRaw) ? planRaw : "starter";
  const definition = PLANS[plan];

  const licenses = listLicenses(user.id);
  const effective = effectivePlan(user, licenses);
  const allowed = countActiveLicenses(user.id);

  if (PLAN_ORDER.indexOf(plan) > PLAN_ORDER.indexOf(effective.id) && user.role !== "admin") {
    redirect("/dashboard/licenses?error=plan_upgrade_required");
  }

  if (allowed >= definition.licenseKeys) {
    redirect("/dashboard/licenses?error=license_limit");
  }

  const seatsRaw = Number(clean(formData.get("seats"), 6));
  const seats = Number.isInteger(seatsRaw) && seatsRaw > 0 ? Math.min(seatsRaw, 500) : definition.seats;
  const notes = clean(formData.get("notes"), 200) || null;
  const expiresRaw = clean(formData.get("expiresAt"), 20);
  const expiresAt = expiresRaw ? new Date(expiresRaw).toISOString() : null;

  const license = createLicense({
    userId: user.id,
    plan,
    seats,
    activationLimit: seats,
    expiresAt,
    notes,
  });

  audit(user.id, "license.issued", { id: license.id, plan, seats });
  revalidatePath("/dashboard/licenses");
  redirect(`/dashboard/licenses?issued=${license.id}`);
}

export async function updateLicenseStatusAction(formData: FormData): Promise<void> {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const back = returnPath(formData, "/dashboard/licenses");
  const id = clean(formData.get("id"), 40);
  const status = clean(formData.get("status"), 20);

  if (!id || !isStatus(status)) redirect(`${back}?error=invalid_request`);

  const license = getDb().prepare("SELECT * FROM licenses WHERE id = ?").get(id) as
    | { user_id: string }
    | undefined;

  if (!license) redirect(`${back}?error=not_found`);
  if (license.user_id !== user.id && user.role !== "admin") {
    redirect(`${back}?error=forbidden`);
  }

  setLicenseStatus(id, status);
  audit(user.id, "license.status", { id, status });
  revalidatePath("/dashboard/admin");
  revalidatePath("/dashboard/licenses");
  redirect(`${back}?updated=1`);
}

function canManage(user: { id: string; role: string }, licenseOwnerId: string): boolean {
  return user.role === "admin" || user.id === licenseOwnerId;
}

function licenseOwnerId(id: string): string | null {
  const row = getDb().prepare("SELECT user_id FROM licenses WHERE id = ?").get(id) as
    | { user_id: string }
    | undefined;
  return row?.user_id ?? null;
}

/** Both the admin area and the owner's own page post these actions. */
function returnPath(formData: FormData, fallback: string): string {
  const raw = clean(formData.get("returnTo"), 60);
  return raw === "/dashboard/licenses" || raw === "/dashboard/admin" ? raw : fallback;
}

/**
 * Admin mints a license. The owner email is optional: leave it empty to create
 * an unassigned key that belongs to whoever claims a seat with it first.
 */
export async function adminIssueLicenseAction(formData: FormData): Promise<void> {
  const user = await getCurrentUser();
  if (!user) redirect("/login");
  if (user.role !== "admin") redirect("/dashboard/admin?error=forbidden");

  const ownerEmail = clean(formData.get("ownerEmail"), 160).toLowerCase();
  const planRaw = clean(formData.get("plan"), 20);
  if (!isPlan(planRaw)) redirect("/dashboard/admin?error=invalid_request");

  const seatsRaw = Number(clean(formData.get("seats"), 6));
  const seats = Number.isInteger(seatsRaw) && seatsRaw > 0 ? Math.min(seatsRaw, 500) : PLANS[planRaw].seats;
  const notes = clean(formData.get("notes"), 200) || null;
  const expiresRaw = clean(formData.get("expiresAt"), 20);
  const expiresAt = expiresRaw ? new Date(expiresRaw).toISOString() : null;

  const result = issueLicenseForUser({ ownerEmail, plan: planRaw, seats, expiresAt, notes });

  if (!result.ok) {
    redirect(`/dashboard/admin?error=${result.error === "user_not_found" ? "no_such_user" : "invalid_request"}`);
  }

  audit(user.id, ownerEmail ? "license.issued_for_user" : "license.issued_unassigned", {
    id: result.license.id,
    owner: ownerEmail || null,
    plan: planRaw,
    seats,
    expiresAt,
  });
  revalidatePath("/dashboard/admin");
  redirect(`/dashboard/admin?issued=${result.license.id}`);
}

/** Change seat count, expiry or notes. Admins may do this on any license. */
export async function updateLicenseTermsAction(formData: FormData): Promise<void> {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const back = returnPath(formData, "/dashboard/admin");
  const id = clean(formData.get("id"), 40);
  if (!id) redirect(`${back}?error=invalid_request`);

  const ownerId = licenseOwnerId(id);
  if (ownerId === null) redirect(`${back}?error=not_found`);
  if (!canManage(user, ownerId)) redirect(`${back}?error=forbidden`);

  const terms: { seats?: number; expiresAt?: string | null; notes?: string | null } = {};

  const seatsRaw = clean(formData.get("seats"), 6);
  if (seatsRaw) {
    const seats = Number(seatsRaw);
    if (Number.isInteger(seats) && seats > 0) terms.seats = Math.min(seats, 500);
  }

  if (formData.has("expiresAt")) {
    const expiresRaw = clean(formData.get("expiresAt"), 20);
    terms.expiresAt = expiresRaw ? new Date(expiresRaw).toISOString() : null;
  }

  if (formData.has("notes")) {
    terms.notes = clean(formData.get("notes"), 200) || null;
  }

  if (Object.keys(terms).length === 0) redirect(`${back}?error=invalid_request`);

  updateLicenseTerms(id, terms);
  audit(user.id, "license.terms", { id, ...terms });
  revalidatePath("/dashboard/admin");
  revalidatePath("/dashboard/licenses");
  redirect(`${back}?updated=1`);
}

/** Free a seat so another account can use the license. Admins or the owner. */
export async function releaseSeatAction(formData: FormData): Promise<void> {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const back = returnPath(formData, "/dashboard/admin");
  const id = clean(formData.get("id"), 40);
  const holder = clean(formData.get("holder"), 160);
  if (!id || !holder) redirect(`${back}?error=invalid_request`);

  const ownerId = licenseOwnerId(id);
  if (ownerId === null) redirect(`${back}?error=not_found`);
  if (!canManage(user, ownerId)) redirect(`${back}?error=forbidden`);

  releaseSeat(id, holder);
  audit(user.id, "license.seat_released", { id, holder });
  revalidatePath("/dashboard/admin");
  revalidatePath("/dashboard/licenses");
  redirect(`${back}?released=1`);
}
