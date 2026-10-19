import { engineUrl } from "@/lib/env";
import { startInspect } from "@/lib/inspect-jobs";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

// Starts a TriStation project (.pt2/.pt2x) inspection.
//
// The browser's multipart upload is forwarded to the engine as raw bytes, never
// parsed: reading it into a FormData and re-encoding it was an order of magnitude
// slower for large projects and held the POST open long enough for the platform
// edge to drop it. The bytes are collected here first, then the engine call runs
// detached, so the request the browser waits on lasts only as long as the upload
// itself -- which is what lets the viewer work from any network.
//
// The POST returns a job id; the viewer polls GET /api/engine/inspect/{id} for the
// model. See lib/inspect-jobs for why the response is split this way.

export async function POST(request: Request) {
  const base = engineUrl();
  if (!base) {
    return Response.json(
      { error: "The conversion engine is not configured, so projects cannot be inspected." },
      { status: 503 },
    );
  }

  const contentType = request.headers.get("content-type") ?? "";
  if (!contentType.toLowerCase().includes("multipart/form-data") || !request.body) {
    return Response.json({ error: "Expected a multipart/form-data upload." }, { status: 400 });
  }

  let body: Buffer;
  try {
    body = Buffer.from(await request.arrayBuffer());
  } catch {
    return Response.json({ error: "The upload was interrupted before it finished." }, { status: 400 });
  }

  if (body.length === 0) {
    return Response.json({ error: "No project file was uploaded." }, { status: 400 });
  }

  const name = new URL(request.url).searchParams.get("name") || "project";
  const job = startInspect(body, contentType, base, name);
  return Response.json({ id: job.id }, { status: 202 });
}
