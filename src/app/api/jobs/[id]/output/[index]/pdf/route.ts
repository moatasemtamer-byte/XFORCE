import fs from "node:fs";
import path from "node:path";
import { NextResponse } from "next/server";
import { getCurrentUser } from "@/lib/auth";
import { getJobForUser, outputsDir } from "@/lib/jobs";
import { engineToken, engineUrl } from "@/lib/env";
import { postEngineBytes } from "@/lib/engine-client";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

// Renders a finished job's stored PT2X output to a PDF. The optional "mode" query selects
// the content: "fbd" (default) mirrors the editor's own FBD sheets, "reports" reproduces
// the report suite from the embedded Telerik .trdp templates, and "both" emits the FBD
// sheets then the reports. The bytes are read from the job folder and forwarded to the
// engine, which draws the PDF; the result is streamed straight back to the browser.
const ENGINE_TIMEOUT_MS = (() => {
  const raw = Number(process.env.MSW_ENGINE_PDF_TIMEOUT_MS);
  return Number.isFinite(raw) && raw > 0 ? raw : 300_000;
})();

const PDF_MODES = new Set(["fbd", "reports", "both"]);

function pdfMode(value: string | null): string {
  const mode = (value ?? "").toLowerCase();
  return PDF_MODES.has(mode) ? mode : "fbd";
}

export async function GET(
  request: Request,
  { params }: RouteContext<"/api/jobs/[id]/output/[index]">,
) {
  const user = await getCurrentUser();
  if (!user) return NextResponse.json({ error: "Sign in required." }, { status: 401 });

  const { id, index } = await params;
  const job = getJobForUser(id, user.id);
  if (!job) return NextResponse.json({ error: "Job not found." }, { status: 404 });

  const position = Number(index);
  const output = job.outputs[position];
  if (!Number.isInteger(position) || !output) {
    return NextResponse.json({ error: "Output not found." }, { status: 404 });
  }

  const resolved = path.resolve(output.path);
  const allowedRoot = path.resolve(outputsDir(user.id, job.id));

  if (!resolved.startsWith(allowedRoot + path.sep)) {
    return NextResponse.json({ error: "Refusing to serve a path outside the job folder." }, { status: 403 });
  }

  if (!fs.existsSync(resolved)) {
    return NextResponse.json({ error: "The stored file is no longer available." }, { status: 410 });
  }

  const base = engineUrl();
  if (!base) {
    return NextResponse.json(
      { error: "The conversion engine is not configured, so projects cannot be rendered." },
      { status: 503 },
    );
  }

  const bytes = fs.readFileSync(resolved);
  const name = output.name || `job-${job.id}.pt2x`;
  const mode = pdfMode(new URL(request.url).searchParams.get("mode"));

  const headers: Record<string, string> = { "content-type": "application/octet-stream" };
  const token = engineToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  const url = `${base.replace(/\/+$/, "")}/api/pdf?name=${encodeURIComponent(name)}&mode=${encodeURIComponent(mode)}`;

  try {
    const response = await postEngineBytes(url, new Uint8Array(bytes), "application/octet-stream", {
      timeoutMs: ENGINE_TIMEOUT_MS,
      headers,
    });

    if (!response.ok) {
      const message =
        (response.json as { error?: string } | null)?.error ??
        `The engine could not render that project (${response.status}).`;
      return NextResponse.json({ error: message }, { status: response.status });
    }

    const pdfName = name.replace(/\.(pt2x?|gz)$/i, "") + ".pdf";
    return new NextResponse(new Uint8Array(response.body), {
      headers: {
        "Content-Type": "application/pdf",
        "Content-Disposition": `attachment; filename="${pdfName.replace(/"/g, "")}"`,
        "Cache-Control": "private, no-store",
      },
    });
  } catch (error) {
    const aborted = error instanceof Error && error.name === "TimeoutError";
    return NextResponse.json(
      {
        error: aborted
          ? `Rendering this project took longer than ${Math.round(ENGINE_TIMEOUT_MS / 1000)}s and timed out.`
          : `The engine could not be reached: ${error instanceof Error ? error.message : "unknown error"}.`,
      },
      { status: aborted ? 504 : 502 },
    );
  }
}
