import { NextResponse } from "next/server";
import { getCurrentUser } from "@/lib/auth";
import {
  featureDeniedMessage,
  grantedFeatures,
  userHasFeature,
} from "@/lib/licensing";
import type { LicenseFeature } from "@/lib/types";

/**
 * Shared guard for the engine endpoints. Everything under /api/engine is behind
 * licensing: the caller must be signed in and hold the required capability.
 * Returns a NextResponse to send back when access is denied, or null to proceed.
 */
export async function requireEngineFeatures(
  required: LicenseFeature[],
): Promise<{ user: Awaited<ReturnType<typeof getCurrentUser>>; response: NextResponse | null }> {
  const user = await getCurrentUser();
  if (!user) {
    return { user: null, response: NextResponse.json({ error: "Sign in to use the workbench." }, { status: 401 }) };
  }

  const missing = required.filter((feature) => !userHasFeature(user.id, feature));
  if (missing.length > 0) {
    return { user, response: NextResponse.json({ error: featureDeniedMessage(missing) }, { status: 403 }) };
  }

  return { user, response: null };
}

/** True when the account has at least one capability, i.e. workbench access. */
export async function hasAnyEngineAccess(userId: string): Promise<boolean> {
  return grantedFeatures(userId).length > 0;
}
