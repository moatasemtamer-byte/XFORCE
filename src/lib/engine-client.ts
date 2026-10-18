import http from "node:http";
import https from "node:https";
import { randomBytes } from "node:crypto";
import { Readable } from "node:stream";

// The engine answers a conversion only when it is finished, so the whole response — headers
// included — arrives after several minutes for a real program. Node's built-in fetch (undici)
// caps the wait for response headers at 300 s regardless of any AbortSignal, which surfaces as
// a bare "fetch failed" for longer runs. This helper posts the multipart form with node:http
// instead, socket timeouts disabled, honouring only the deadline the caller passes.

export interface EngineResponse {
  ok: boolean;
  status: number;
  json: unknown;
  raw: string;
}

export class EngineTimeoutError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "TimeoutError";
  }
}

function escapeDisposition(value: string): string {
  return value.replace(/["\r\n]/g, "_");
}

// Chunks the multipart body as a stream so a large upload is never held fully in memory.
async function* multipartParts(form: FormData, boundary: string): AsyncGenerator<Buffer> {
  for (const [name, value] of form.entries()) {
    if (typeof value === "string") {
      yield Buffer.from(
        `--${boundary}\r\nContent-Disposition: form-data; name="${escapeDisposition(name)}"\r\n\r\n` +
          `${value}\r\n`,
        "utf8",
      );
      continue;
    }

    const filename = escapeDisposition(value.name || "upload");
    yield Buffer.from(
      `--${boundary}\r\nContent-Disposition: form-data; name="${escapeDisposition(name)}"; ` +
        `filename="${filename}"\r\nContent-Type: ${value.type || "application/octet-stream"}\r\n\r\n`,
      "utf8",
    );

    const chunkSize = 64 * 1024;
    for (let offset = 0; offset < value.size; offset += chunkSize) {
      const slice = value.slice(offset, offset + chunkSize);
      yield Buffer.from(await slice.arrayBuffer());
    }
    yield Buffer.from("\r\n", "utf8");
  }
  yield Buffer.from(`--${boundary}--\r\n`, "utf8");
}

export async function postEngine(
  endpoint: string,
  form: FormData,
  options: { timeoutMs: number; headers?: Record<string, string> },
): Promise<EngineResponse> {
  const url = new URL(endpoint);
  const boundary = `----msw-${randomBytes(16).toString("hex")}`;
  const transport = url.protocol === "https:" ? https : http;

  return new Promise((resolve, reject) => {
    const request = transport.request(
      {
        protocol: url.protocol,
        hostname: url.hostname,
        port: url.port || (url.protocol === "https:" ? 443 : 80),
        path: `${url.pathname}${url.search}`,
        method: "POST",
        headers: {
          ...options.headers,
          "content-type": `multipart/form-data; boundary=${boundary}`,
        },
      },
      (response) => {
        const parts: Buffer[] = [];
        response.on("data", (chunk: Buffer) => parts.push(chunk));
        response.on("end", () => {
          clearTimeout(deadline);
          const raw = Buffer.concat(parts).toString("utf8");
          let json: unknown = null;
          try {
            json = raw ? JSON.parse(raw) : null;
          } catch {
            json = null;
          }
          const status = response.statusCode ?? 500;
          resolve({ ok: status >= 200 && status < 300, status, json, raw });
        });
        response.on("error", (error) => {
          clearTimeout(deadline);
          reject(error);
        });
      },
    );

    // No socket idle timeout: the caller's deadline is the only limit.
    request.setTimeout(0);

    const deadline = setTimeout(() => {
      request.destroy();
      reject(new EngineTimeoutError("the engine did not finish before the deadline"));
    }, options.timeoutMs);

    request.on("error", (error) => {
      clearTimeout(deadline);
      reject(error);
    });

    Readable.from(multipartParts(form, boundary)).pipe(request);
  });
}
