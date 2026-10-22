import { engineToken, engineUrl } from "@/lib/env";
import { postEngineBytes } from "@/lib/engine-client";
import { requireEngineFeatures } from "@/lib/engine-guard";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

// Renders a TriStation project (.pt2/.pt2x) to a PDF. The optional "mode" query selects the
// content: "fbd" (default) mirrors the editor's own FBD sheets, "reports" reproduces the
// report suite (POU Variables, Instance Variable Connections, Program Variable Listing,
// Tagnames Complete Listing, POU Cross-Reference, Shared Libraries, Project Implementation)
// from the embedded Telerik .trdp templates, and "both" emits the FBD sheets then the
// reports. The browser's multipart upload is forwarded to the engine as raw bytes (never
// re-encoded), and the PDF bytes are returned straight back so the browser can display or
// download them. Gated on the "viewer" capability, like /api/engine/inspect.

const ENGINE_TIMEOUT_MS = (() => {
  const raw = Number(process.env.MSW_ENGINE_PDF_TIMEOUT_MS);
  return Number.isFinite(raw) && raw > 0 ? raw : 300_000;
})();

const PDF_MODES = new Set(["fbd", "reports", "both"]);

export function pdfModeFromRequest(value: string | null): string {
  const mode = (value ?? "").toLowerCase();
  return PDF_MODES.has(mode) ? mode : "fbd";
}

export async function POST(request: Request) {
  const gate = await requireEngineFeatures(["viewer"]);
  if (gate.response) return gate.response;

  const base = engineUrl();
  if (!base) {
    return Response.json(
      { error: "The conversion engine is not configured, so projects cannot be rendered." },
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

  const search = new URL(request.url).searchParams;
  const name = search.get("name") || "project";
  const mode = pdfModeFromRequest(search.get("mode"));
  const headers: Record<string, string> = {};
  const token = engineToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  const url = `${base.replace(/\/+$/, "")}/api/pdf?name=${encodeURIComponent(name)}&mode=${encodeURIComponent(mode)}`;

  try {
    const response = await postEngineBytes(url, body, contentType, {
      timeoutMs: ENGINE_TIMEOUT_MS,
      headers,
    });

    if (!response.ok) {
      const message =
        (response.json as { error?: string } | null)?.error ??
        `The engine could not render that project (${response.status}).`;
      return Response.json({ error: message }, { status: response.status });
    }

    return new Response(new Uint8Array(response.body), {
      status: 200,
      headers: {
        "content-type": response.contentType ?? "application/pdf",
        "content-disposition": "inline",
        "cache-control": "no-store",
      },
    });
  } catch (error) {
    const aborted = error instanceof Error && error.name === "TimeoutError";
    return Response.json(
      {
        error: aborted
          ? `Rendering this project took longer than ${Math.round(ENGINE_TIMEOUT_MS / 1000)}s and timed out.`
          : `The engine could not be reached: ${error instanceof Error ? error.message : "unknown error"}.`,
      },
      { status: aborted ? 504 : 502 },
    );
  }
}
