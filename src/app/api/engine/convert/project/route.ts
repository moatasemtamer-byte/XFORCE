import { NextResponse } from "next/server";
import { engineToken, engineUrl } from "@/lib/env";
import { EngineTimeoutError, postEngine } from "@/lib/engine-client";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

// Forwards a source upload to the engine's /convert/project endpoint. That path converts the
// source and then drives TIA Portal (Siemens Openness) to build a real, openable .ap16 project,
// returned to the browser as a zip of the project folder. The import runs for minutes, so this
// route uses a far longer timeout than /api/engine/convert and only works when the engine host
// has TIA Portal installed.

const MAX_BYTES = Number(process.env.MSW_MAX_UPLOAD_BYTES ?? 512 * 1024 * 1024);
const TIMEOUT_MS = Number(process.env.MSW_ENGINE_PROJECT_TIMEOUT_MS ?? 2_700_000);

interface RemoteArtifact {
  name?: string;
  mimeType?: string;
  kind?: string;
  contentBase64?: string;
}

export async function POST(request: Request) {
  const base = engineUrl();
  if (!base) {
    return NextResponse.json(
      { error: "MSW_ENGINE_URL is not configured, so conversions cannot be dispatched." },
      { status: 503 },
    );
  }

  let form: FormData;
  try {
    form = await request.formData();
  } catch {
    return NextResponse.json({ error: "Malformed upload." }, { status: 400 });
  }

  const file = form.get("file");
  if (!(file instanceof File) || file.size === 0) {
    return NextResponse.json({ error: "Choose a source file to upload." }, { status: 400 });
  }
  if (file.size > MAX_BYTES) {
    return NextResponse.json(
      { error: `That file exceeds the ${Math.round(MAX_BYTES / (1024 * 1024))} MB limit.` },
      { status: 413 },
    );
  }

  const forwarded = new FormData();
  forwarded.set("file", file, file.name);
  forwarded.set("options", typeof form.get("options") === "string" ? (form.get("options") as string) : "{}");
  const notes = form.get("notes");
  if (typeof notes === "string") forwarded.set("notes", notes);

  const token = engineToken();
  const headers: Record<string, string> = {};
  if (token) headers.Authorization = `Bearer ${token}`;

  let remote: Awaited<ReturnType<typeof postEngine>>;
  try {
    remote = await postEngine(`${base.replace(/\/+$/, "")}/convert/project`, forwarded, {
      timeoutMs: TIMEOUT_MS,
      headers,
    });
  } catch (error) {
    const reason = error instanceof Error ? error.message : "unknown error";
    const timedOut = error instanceof EngineTimeoutError;
    return NextResponse.json(
      {
        error: timedOut
          ? "The engine did not finish building the TIA project in time."
          : `Could not reach the engine: ${reason}`,
      },
      { status: 502 },
    );
  }

  if (!remote.ok) {
    const detail = remote.json as
      | { error?: string; log?: string; projectName?: string }
      | null;
    return NextResponse.json(
      {
        error: detail?.error ?? `Engine returned ${remote.status}.`,
        log: detail?.log,
        projectName: detail?.projectName,
      },
      { status: remote.status === 401 ? 502 : remote.status },
    );
  }

  const payload = remote.json as
    | {
        artifacts?: RemoteArtifact[];
        warnings?: string[];
        summary?: Record<string, unknown>;
        projectName?: string;
        engineVersion?: string;
      }
    | null;

  if (!payload) {
    return NextResponse.json({ error: "Engine returned an unreadable response." }, { status: 502 });
  }

  const artifacts = (payload.artifacts ?? []).filter(
    (artifact): artifact is RemoteArtifact & { contentBase64: string } =>
      typeof artifact.contentBase64 === "string" && artifact.contentBase64.length > 0,
  );

  if (artifacts.length === 0) {
    return NextResponse.json({ error: "Engine returned no artifacts." }, { status: 502 });
  }

  return NextResponse.json({
    artifacts: artifacts.map((artifact) => ({
      name: artifact.name ?? "artifact",
      mimeType: artifact.mimeType ?? "application/octet-stream",
      kind: artifact.kind ?? "primary",
      contentBase64: artifact.contentBase64,
    })),
    warnings: payload.warnings ?? [],
    summary: payload.summary ?? {},
    projectName: payload.projectName ?? null,
    engineVersion: payload.engineVersion ?? null,
  });
}
