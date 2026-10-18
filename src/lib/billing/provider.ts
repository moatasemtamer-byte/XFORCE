/**
 * Billing seam.
 *
 * The application never talks to a payment provider directly. It builds a
 * CheckoutRequest, hands it to the active BillingProvider, and acts on the
 * CheckoutResult. Replacing the mock with Stripe later means writing one new
 * provider and changing the resolver in ./index.ts — no call site changes.
 */

export type PlanId = "free" | "starter" | "professional" | "enterprise";
export type BillingCycle = "monthly" | "yearly";

export interface CheckoutRequest {
  plan: PlanId;
  cycle: BillingCycle;
  email: string;
  /** Opaque id echoed back so a webhook can find the order again. */
  reference: string;
  successUrl: string;
  cancelUrl: string;
}

export type CheckoutResult =
  | { ok: true; provider: string; paymentId: string; reference: string; amount: number; currency: string }
  | { ok: false; provider: string; reason: string };

export interface BillingProvider {
  readonly id: string;
  /** False means this provider is not usable in the current configuration. */
  readonly available: boolean;
  createCheckout(request: CheckoutRequest): Promise<CheckoutResult>;
  /** Set for hosted checkout (Stripe would return a redirect URL here). */
  readonly redirectUrl?: (result: Extract<CheckoutResult, { ok: true }>) => string | null;
}

export const CURRENCY = "USD";
