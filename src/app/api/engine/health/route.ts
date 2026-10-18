import { NextResponse } from "next/server";
import { engineToken, engineUrl } from "@/lib/env";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

// Reports whether the engine host can build TIA projects. Used by the convert form to enable the
// "build an openable project" option only where TIA Portal is actually installed.

export async function GET() {
  const base = engineUrl();
  if (!base) {
    return NextResponse.json(
      { available: false, tiaImport: null, error: "MSW_ENGINE_URL is not configured." },
      { status: 503 },
    );
  }

  const headers: Record<string, string> = {};
  const token = engineToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  try {
    const response = await fetch(`${base.replace(/\/+$/, "")}/health`, {
      headers,
      cache: "no-store",
      signal: AbortSignal.timeout(8000),
    });
    if (!response.ok) {
      return NextResponse.json(
        { available: false, tiaImport: null, error: `Engine returned ${response.status}.` },
        { status: 502 },
      );
    }
    const payload = (await response.json()) as {
      engineVersion?: string;
      tiaImport?: { available?: boolean; installPath?: string; cpuOrderNumber?: string | null };
    };
    return NextResponse.json({
      available: true,
      tiaImport: payload.tiaImport ?? null,
      engineVersion: payload.engineVersion ?? null,
    });
  } catch (error) {
    return NextResponse.json(
      { available: false, tiaImport: null, error: error instanceof Error ? error.message : "unreachable" },
      { status: 502 },
    );
  }
}
