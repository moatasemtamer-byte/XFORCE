"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { audit, getCurrentUser } from "@/lib/auth";
import { getDb } from "@/lib/db";
import {
  countActiveLicenses,
  createLicense,
  effectivePlan,
  listLicenses,
  PLANS,
  PLAN_ORDER,
  setLicenseStatus,
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

  const id = clean(formData.get("id"), 40);
  const status = clean(formData.get("status"), 20);

  if (!id || !isStatus(status)) redirect("/dashboard/licenses?error=invalid_request");

  const license = getDb().prepare("SELECT * FROM licenses WHERE id = ?").get(id) as
    | { user_id: string }
    | undefined;

  if (!license) redirect("/dashboard/licenses?error=not_found");
  if (license.user_id !== user.id && user.role !== "admin") {
    redirect("/dashboard/licenses?error=forbidden");
  }

  setLicenseStatus(id, status);
  audit(user.id, "license.status", { id, status });
  revalidatePath("/dashboard/licenses");
  redirect("/dashboard/licenses");
}
