import { getInspect } from "@/lib/inspect-jobs";
import { requireEngineFeatures } from "@/lib/engine-guard";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

// Reports one inspection started by POST /api/engine/inspect. The viewer polls
// this while the engine reads the project. A 404 means the id is unknown or the
// job already finished and was swept, which the client treats as a failed job.

export async function GET(_request: Request, context: RouteContext<"/api/engine/inspect/[id]">) {
  const gate = await requireEngineFeatures(["viewer"]);
  if (gate.response) return gate.response;

  const { id } = await context.params;
  const job = getInspect(id);

  if (!job) {
    return Response.json(
      { status: "error", error: "That inspection is no longer available. Drop the project again." },
      { status: 404 },
    );
  }

  return Response.json(
    {
      status: job.status,
      payload: job.status === "ok" ? job.payload : undefined,
      error: job.status === "error" ? job.error : undefined,
      errorStatus: job.status === "error" ? job.errorStatus : undefined,
      elapsedMs: Date.now() - job.startedAt,
    },
    { headers: { "Cache-Control": "no-store" } },
  );
}
