import { engineToken, engineUrl } from "../env";
import {
  ConversionInput,
  ConversionOptions,
  ConversionOutcome,
  ConverterAdapter,
  EngineStatus,
  EngineUnavailableError,
} from "./types";

interface RemoteArtifact {
  name?: string;
  filename?: string;
  mimeType?: string;
  mime_type?: string;
  kind?: string;
  contentBase64?: string;
  content_base64?: string;
  content?: string;
  url?: string;
}

interface RemoteResponse {
  artifacts?: RemoteArtifact[];
  outputs?: RemoteArtifact[];
  warnings?: string[];
  summary?: Record<string, unknown>;
  engineVersion?: string;
  engine_version?: string;
}

const ARTIFACT_KINDS = new Set(["primary", "report", "netlist", "bom", "preview"]);

function artifactKind(raw: string | undefined, index: number): ConversionOutcome["artifacts"][number]["kind"] {
  if (raw && ARTIFACT_KINDS.has(raw)) return raw as "primary";
  if (index === 0) return "primary";
  return "report";
}

export class HttpEngineAdapter implements ConverterAdapter {
  readonly id = "msw-engine-http";
  readonly label = "MSW engine service";
  readonly version = "1";

  private readonly baseUrl: string;
  private readonly token: string | null;

  constructor(baseUrl: string, token: string | null) {
    this.baseUrl = baseUrl.replace(/\/+$/, "");
    this.token = token;
  }

  static fromEnv(): HttpEngineAdapter | null {
    const url = engineUrl();
    if (!url) return null;
    return new HttpEngineAdapter(url, engineToken());
  }

  async available(): Promise<boolean> {
    return true;
  }

  async status(): Promise<EngineStatus> {
    // Actually probe the engine rather than assuming it is up, so the badge on
    // the marketing pages and in the dashboard cannot claim a connection that
    // does not work. A failed probe degrades the copy instead of throwing.
    const healthUrl = `${this.baseUrl}/health`;
    try {
      const response = await fetch(healthUrl, {
        headers: this.token ? { Authorization: `Bearer ${this.token}` } : {},
        signal: AbortSignal.timeout(Number(process.env.MSW_ENGINE_HEALTH_TIMEOUT_MS ?? 5000)),
        cache: "no-store",
      });

      if (!response.ok) {
        return {
          ready: false,
          adapterId: this.id,
          label: this.label,
          version: this.version,
          detail: `Engine health check returned ${response.status} from ${healthUrl}. Conversions will fail until this is resolved.`,
        };
      }

      const body = (await response.json().catch(() => null)) as
        | { engineVersion?: string; maxUploadBytes?: number; maxPages?: number }
        | null;
      const parts = [`Health check OK at ${healthUrl}`];
      if (body?.engineVersion) parts.push(`engine ${body.engineVersion}`);
      if (typeof body?.maxUploadBytes === "number") {
        parts.push(`${Math.round(body.maxUploadBytes / (1024 * 1024))} MB upload cap`);
      }
      if (typeof body?.maxPages === "number") parts.push(`${body.maxPages} page cap`);

      return {
        ready: true,
        adapterId: this.id,
        label: this.label,
        version: body?.engineVersion ?? this.version,
        detail: `${parts.join(" · ")}. Conversions are dispatched to ${this.baseUrl}/convert.`,
      };
    } catch (error) {
      return {
        ready: false,
        adapterId: this.id,
        label: this.label,
        version: this.version,
        detail: `Could not reach the engine health check at ${healthUrl}: ${
          error instanceof Error ? error.message : "unknown error"
        }. Conversions will fail until this is resolved.`,
      };
    }
  }

  async convert(input: ConversionInput, options: ConversionOptions): Promise<ConversionOutcome> {
    const form = new FormData();
    form.set("jobId", input.jobId);
    form.set("filename", input.filename);
    form.set("licenseKey", input.licenseKey ?? "");
    form.set("options", JSON.stringify(options));
    form.set("file", new Blob([new Uint8Array(input.bytes)], { type: input.mimeType }), input.filename);

    const headers: Record<string, string> = {};
    if (this.token) headers.Authorization = `Bearer ${this.token}`;

    const timeoutMs = Number(process.env.MSW_ENGINE_TIMEOUT_MS ?? 120_000);

    let response: Response;
    try {
      response = await fetch(`${this.baseUrl}/convert`, {
        method: "POST",
        body: form,
        headers,
        signal: AbortSignal.timeout(timeoutMs),
      });
    } catch (error) {
      throw new EngineUnavailableError(
        "engine_unreachable",
        `Could not reach the engine service: ${error instanceof Error ? error.message : "unknown error"}`,
      );
    }

    const contentType = response.headers.get("content-type") ?? "";

    if (!response.ok) {
      const detail = await response.text().catch(() => "");
      throw new EngineUnavailableError(
        "engine_error",
        `Engine returned ${response.status}. ${detail.slice(0, 400)}`,
      );
    }

    if (!contentType.includes("application/json")) {
      const bytes = Buffer.from(await response.arrayBuffer());
      const name = new URL(response.url || `${this.baseUrl}`).pathname.split("/").pop() || "result";
      return {
        artifacts: [
          {
            name: name.endsWith(".") ? name : `${name}.bin`,
            bytes,
            mimeType: contentType || "application/octet-stream",
            kind: "primary",
          },
        ],
        warnings: [],
        summary: { transport: "binary" },
        engineVersion: this.version,
      };
    }

    const payload = (await response.json()) as RemoteResponse;
    const raw = payload.artifacts ?? payload.outputs ?? [];
    const artifacts = await Promise.all(
      raw.map(async (item, index) => {
        const base64 = item.contentBase64 ?? item.content_base64 ?? item.content;
        if (base64) {
          return {
            name: item.name ?? item.filename ?? `artifact-${index + 1}`,
            bytes: Buffer.from(base64, "base64"),
            mimeType: item.mimeType ?? item.mime_type ?? "application/octet-stream",
            kind: artifactKind(item.kind, index),
          };
        }
        if (item.url) {
          const file = await fetch(item.url);
          if (!file.ok) {
            throw new EngineUnavailableError("engine_error", `Could not download artifact ${item.url}`);
          }
          return {
            name: item.name ?? item.filename ?? new URL(item.url).pathname.split("/").pop() ?? `artifact-${index + 1}`,
            bytes: Buffer.from(await file.arrayBuffer()),
            mimeType: item.mimeType ?? item.mime_type ?? file.headers.get("content-type") ?? "application/octet-stream",
            kind: artifactKind(item.kind, index),
          };
        }
        throw new EngineUnavailableError("engine_error", "Engine returned an artifact with neither content nor url.");
      }),
    );

    if (artifacts.length === 0) {
      throw new EngineUnavailableError("engine_error", "Engine returned no artifacts.");
    }

    return {
      artifacts,
      warnings: payload.warnings ?? [],
      summary: payload.summary ?? {},
      engineVersion: payload.engineVersion ?? payload.engine_version ?? this.version,
    };
  }
}
