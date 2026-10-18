import { newId } from "@/lib/util";
import { mockBillingEnabled, mockBillingToken } from "@/lib/env";
import type { BillingProvider, CheckoutRequest, CheckoutResult } from "./provider";

/**
 * Mock payment provider.
 *
 * It performs no network call and collects no card details. It "approves" any
 * well-formed request, which is exactly why it is gated twice: the resolver
 * only returns it when MOCK_BILLING_TOKEN is configured, and the caller has to
 * echo that token. Treat every license it issues as a test artifact.
 */
export const mockProvider: BillingProvider = {
  id: "mock",
  available: mockBillingEnabled(),

  async createCheckout(request: CheckoutRequest): Promise<CheckoutResult> {
    if (!mockBillingToken()) {
      return { ok: false, provider: "mock", reason: "Mock billing is not enabled on this server." };
    }
    if (!request.email.includes("@")) {
      return { ok: false, provider: "mock", reason: "A valid email address is required." };
    }
    return {
      ok: true,
      provider: "mock",
      paymentId: `mock_${newId("pay")}`,
      reference: request.reference,
      // The caller substitutes the real plan price; 0 means "not priced here".
      amount: 0,
      currency: "USD",
    };
  },
};
