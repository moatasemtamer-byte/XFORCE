/**
 * Copy for the redirect codes the access actions emit.
 *
 * Kept out of the "use server" modules (which may only export async
 * functions) so the pages can render the same messages.
 */
const REDEEM_ERRORS: Record<string, string> = {
  missing_key: "Enter a license key.",
  unknown_key: "That key is not recognised.",
  revoked: "That license has been revoked.",
  suspended: "That license is suspended.",
  expired: "That license has expired.",
  seats_full: "Every seat on that license is already in use.",
  email: "Enter a valid email address.",
  rate_limited: "Too many attempts. Try again in a few minutes.",
};

const CHECKOUT_ERRORS: Record<string, string> = {
  email: "Enter a valid email address.",
  payment: "The payment could not be completed. Try again.",
  unavailable: "Checkout is not available on this server right now.",
  rate_limited: "Too many attempts. Try again in a few minutes.",
  invalid_plan: "That plan is not available.",
};

export function accessErrorMessage(code: string | undefined): string | null {
  if (!code) return null;
  return REDEEM_ERRORS[code] ?? null;
}

export function checkoutErrorMessage(code: string | undefined): string | null {
  if (!code) return null;
  return CHECKOUT_ERRORS[code] ?? null;
}
