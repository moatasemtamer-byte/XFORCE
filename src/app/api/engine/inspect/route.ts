import { NextResponse } from "next/server";
import { engineToken, engineUrl } from "@/lib/env";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

// Proxies a TriStation project (.pt2/.pt2x) to the engine's /api/inspect so the
// browser never talks to the engine directly and the bearer token stays server-side.
// The engine answers with the JSON model the viewer renders.

export async function POST(request: Request) {
  const base = engineUrl();
  if (!base) {
    return NextResponse.json({ error: "MSW_ENGINE_URL is not configured." }, { status: 503 });
  }

  const form = await request.formData().catch(() => null);
  const file = form?.get("file");
  if (!(file instanceof File) || file.size === 0) {
    return NextResponse.json({ error: "No project file was uploaded." }, { status: 400 });
  }

  const headers: Record<string, string> = {};
  const token = engineToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  try {
    const response = await fetch(`${base.replace(/\/+$/, "")}/api/inspect`, {
      method: "POST",
      headers,
      body: form,
      cache: "no-store",
      signal: AbortSignal.timeout(120_000),
    });
    const payload = await response.json().catch(() => null);
    if (!response.ok) {
      return NextResponse.json(payload ?? { error: "The engine could not open that project." }, { status: response.status });
    }
    return NextResponse.json(payload);
  } catch {
    return NextResponse.json({ error: "The engine could not be reached." }, { status: 502 });
  }
}
