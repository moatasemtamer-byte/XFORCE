import { NextResponse } from "next/server";
import { activateLicense, checkLicenseKey } from "@/lib/licensing";
import { clientKey, rateLimit } from "@/lib/rate-limit";
import { clean } from "@/lib/validate";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

function response(body: Record<string, unknown>, status = 200) {
  return NextResponse.json(body, { status });
}

export async function GET(request: Request) {
  const key = new URL(request.url).searchParams.get("key") ?? "";
  const limit = rateLimit(clientKey(["license-validate", key]), 120, 60_000);
  if (!limit.ok) return response({ valid: false, reason: "rate_limited" }, 429);

  const check = checkLicenseKey(key);
  if (!check.valid || !check.license) {
    return response({ valid: false, reason: check.reason ?? "invalid" }, check.reason === "unknown_key" ? 404 : 200);
  }

  return response({
    valid: true,
    plan: check.license.plan,
    seats: check.license.activation_limit,
    activations: check.license.activations,
    expiresAt: check.license.expires_at,
  });
}

export async function POST(request: Request) {
  const ip = request.headers.get("x-forwarded-for")?.split(",")[0]?.trim() ?? null;
  const limit = rateLimit(clientKey(["license-activate", ip]), 30, 60_000);
  if (!limit.ok) return response({ valid: false, reason: "rate_limited" }, 429);

  let payload: { key?: string; device?: string };
  try {
    payload = (await request.json()) as { key?: string; device?: string };
  } catch {
    return response({ valid: false, reason: "invalid_json" }, 400);
  }

  const key = clean(payload.key ?? "", 80);
  const device = clean(payload.device ?? "", 120) || null;

  const check = checkLicenseKey(key);
  if (!check.valid || !check.license) {
    return response({ valid: false, reason: check.reason ?? "invalid" }, 403);
  }

  activateLicense(check.license.id, device);

  return response({
    valid: true,
    plan: check.license.plan,
    activations: check.license.activations + 1,
    activationLimit: check.license.activation_limit,
    expiresAt: check.license.expires_at,
  });
}
