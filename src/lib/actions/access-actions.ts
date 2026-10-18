"use server";

import { headers } from "next/headers";
import { redirect } from "next/navigation";
import { billingProvider } from "@/lib/billing";
import { audit, createSession, isPasswordless, provisionLicenseAccount } from "@/lib/auth";
import { mockBillingToken } from "@/lib/env";
import {
  claimSeat,
  checkLicenseKey,
  createLicense,
  PLANS,
  PLAN_ORDER,
} from "@/lib/licensing";
import { clientKey, rateLimit } from "@/lib/rate-limit";
import { clean, isEmail } from "@/lib/validate";
import type { PlanId } from "@/lib/types";

function isPlan(value: string): value is PlanId {
  return (PLAN_ORDER as string[]).includes(value);
}

async function requestMeta(): Promise<{ userAgent: string | null; ip: string | null }> {
  const h = await headers();
  const forwarded = h.get("x-forwarded-for");
  return {
    userAgent: h.get("user-agent"),
    ip: forwarded ? forwarded.split(",")[0].trim() : h.get("x-real-ip"),
  };
}

/**
 * License-first access: paste a key, get in. No registration form.
 *
 * The key is the credential, so we provision the account for the given email,
 * claim a seat against it, and open a session. The email only names the
 * account.
 *
 * One hard rule: if the email already belongs to an account that has a real
 * password, we must not mint a session for it. Otherwise anyone holding a key
 * plus a known email address could walk straight into somebody else's account.
 * In that case the license is still linked to the account, but the visitor is
 * sent to sign in properly.
 */
export async function redeemLicenseKeyAction(formData: FormData): Promise<void> {
  const key = clean(formData.get("key"), 80);
  const email = clean(formData.get("email"), 160).toLowerCase();
  const meta = await requestMeta();

  if (!isEmail(email)) redirect("/enter-license?error=email");

  const limit = rateLimit(clientKey(["redeem", meta.ip]), 12, 15 * 60_000);
  if (!limit.ok) redirect("/enter-license?error=rate_limited");

  const check = checkLicenseKey(key);
  if (!check.valid || !check.license) {
    audit(null, "license.redeem_failed", { reason: check.reason });
    redirect(`/enter-license?error=${check.reason ?? "unknown_key"}`);
  }

  const account = provisionLicenseAccount(email);
  const claim = claimSeat(check.license.id, account.email, "account", account.id);

  if (!claim.ok) {
    audit(account.id, "license.redeem_failed", { license: check.license.id, reason: claim.reason });
    redirect(`/enter-license?error=${claim.reason === "activation_limit" ? "seats_full" : "unknown_key"}`);
  }

  if (!isPasswordless(account)) {
    audit(account.id, "license.redeem_awaiting_login", { license: check.license.id });
    redirect("/login?error=sign_in_required");
  }

  await createSession(account.id, { remember: true, ...meta });
  audit(account.id, "license.redeemed", {
    license: check.license.id,
    plan: check.license.plan,
    seat: claim.reason,
  });

  redirect("/dashboard?welcome=1");
}

/**
 * Buy a license with the mock provider.
 *
 * No card is collected and no network call is made. The result is a real
 * license row, issued to a real account, so the rest of the pipeline (seats,
 * entitlements, admin visibility) behaves exactly as it would after a genuine
 * payment. A Stripe provider would implement the same BillingProvider
 * interface, and a webhook would call issueLicenseForUser instead of this.
 */
export async function checkoutAction(formData: FormData): Promise<void> {
  const planRaw = clean(formData.get("plan"), 20);
  const cycle = clean(formData.get("cycle"), 10) === "yearly" ? "yearly" : "monthly";
  const email = clean(formData.get("email"), 160).toLowerCase();
  const token = clean(formData.get("token"), 120);
  const meta = await requestMeta();

  if (!isPlan(planRaw)) redirect("/pricing?error=invalid_plan");
  if (planRaw === "enterprise") redirect("/contact?source=enterprise");

  const plan = PLANS[planRaw];
  if (plan.priceMonthly === null) redirect("/contact?source=enterprise");

  if (!isEmail(email)) redirect(`/checkout?plan=${planRaw}&cycle=${cycle}&error=email`);

  const account = provisionLicenseAccount(email);

  // The free plan has nothing to charge, so it skips the payment step. It is
  // still a real license row, so seats and entitlements work identically.
  const isFree = plan.priceMonthly === 0;

  const expected = mockBillingToken();
  if (!isFree && (!expected || token !== expected)) {
    audit(null, "billing.checkout_refused", { plan: planRaw, reason: expected ? "bad_token" : "disabled" });
    redirect(`/checkout?plan=${planRaw}&cycle=${cycle}&error=unavailable`);
  }

  if (!isFree) {
    const limit = rateLimit(clientKey(["checkout", meta.ip]), 10, 15 * 60_000);
    if (!limit.ok) redirect(`/checkout?plan=${planRaw}&cycle=${cycle}&error=rate_limited`);

    const result = await billingProvider().createCheckout({
      plan: planRaw,
      cycle,
      email,
      reference: account.id,
      successUrl: "/dashboard",
      cancelUrl: "/pricing",
    });

    if (!result.ok) {
      audit(null, "billing.checkout_failed", { plan: planRaw, reason: result.reason });
      redirect(`/checkout?plan=${planRaw}&cycle=${cycle}&error=payment`);
    }

    audit(account.id, "billing.checkout_completed", {
      paymentId: result.paymentId,
      provider: result.provider,
      plan: planRaw,
      cycle,
    });
  } else {
    audit(account.id, "billing.free_started", { plan: planRaw });
  }

  const license = createLicense({
    userId: account.id,
    plan: planRaw,
    seats: plan.seats,
    activationLimit: plan.seats,
    notes: isFree ? "Free plan" : "Issued after mock checkout",
  });

  audit(account.id, "license.issued", { license: license.id, plan: planRaw, via: "checkout" });

  // Same rule as redemption: a key or a purchase must never mint a session for
  // an account that already has a password. The license is issued either way;
  // the visitor just has to prove who they are first.
  if (!isPasswordless(account)) {
    redirect("/login?error=sign_in_required");
  }

  await createSession(account.id, { remember: true, ...meta });

  redirect(`/enter-license?issued=${license.id}&plan=${planRaw}`);
}
