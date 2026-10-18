import { NextResponse } from "next/server";
import { engineToken, engineUrl } from "@/lib/env";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

// Proxies the engine's conversion history (history.jsonl) so the convert portal can show
// what has been converted without the browser talking to the engine directly.

export async function GET(request: Request) {
  const base = engineUrl();
  if (!base) {
    return NextResponse.json({ entries: [], error: "MSW_ENGINE_URL is not configured." }, { status: 503 });
  }

  const limit = new URL(request.url).searchParams.get("limit") ?? "50";

  const headers: Record<string, string> = {};
  const token = engineToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  try {
    const response = await fetch(`${base.replace(/\/+$/, "")}/api/history?limit=${encodeURIComponent(limit)}`, {
      headers,
      cache: "no-store",
      signal: AbortSignal.timeout(8000),
    });
    const payload = await response.json().catch(() => null);
    if (!response.ok) {
      return NextResponse.json(payload ?? { entries: [] }, { status: response.status });
    }
    return NextResponse.json(payload ?? { entries: [] });
  } catch {
    return NextResponse.json({ entries: [] });
  }
}
