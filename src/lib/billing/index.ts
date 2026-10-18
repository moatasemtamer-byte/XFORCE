import { mockBillingEnabled } from "@/lib/env";
import { mockProvider } from "./mock";
import type { BillingProvider } from "./provider";

export * from "./provider";

/**
 * The active payment provider.
 *
 * Today this is always the mock. When a real provider is added it is selected
 * here — nothing else in the application needs to know the difference.
 */
export function billingProvider(): BillingProvider {
  return mockProvider;
}

export function billingAvailable(): boolean {
  return mockBillingEnabled() && billingProvider().available;
}
