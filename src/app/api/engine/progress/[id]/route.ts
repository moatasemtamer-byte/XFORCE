import { NextResponse } from "next/server";
import { engineToken, engineUrl } from "@/lib/env";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

// Proxies one in-flight conversion's progress from the engine. The convert form generates a
// random id, sends it with the upload, and polls this route while the POST is still running.
// Progress is in-memory on the engine and expires after an hour; a 404 simply means the poll
// lost the race with the response, which the caller ignores.

export async function GET(_request: Request, context: RouteContext<"/api/engine/progress/[id]">) {
  const base = engineUrl();
  if (!base) {
    return NextResponse.json({ error: "MSW_ENGINE_URL is not configured." }, { status: 503 });
  }

  const { id } = await context.params;
  if (!/^[a-zA-Z0-9-]{1,64}$/.test(id)) {
    return NextResponse.json({ error: "Malformed progress id." }, { status: 400 });
  }

  const headers: Record<string, string> = {};
  const token = engineToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  try {
    const response = await fetch(`${base.replace(/\/+$/, "")}/api/progress/${encodeURIComponent(id)}`, {
      headers,
      cache: "no-store",
      signal: AbortSignal.timeout(5000),
    });
    const payload = await response.json().catch(() => null);
    return NextResponse.json(payload ?? {}, { status: response.status });
  } catch {
    return NextResponse.json({ error: "unreachable" }, { status: 502 });
  }
}
