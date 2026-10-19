import "server-only";
import { engineToken } from "@/lib/env";
import { postEngineRaw } from "@/lib/engine-client";

// In-memory registry of in-flight project inspections for the viewer.
//
// Why a job and not a single streamed request: the engine decompiles a large
// .pt2x synchronously and can take ~a minute. Any response that holds the
// browser's connection that long gets buffered and eventually dropped by the
// platform edge, so the POST answers immediately with an id and the slow work
// runs detached. The viewer polls the matching GET route.
//
// This lives in the web service's long-running Node process (Railway runs the
// Next.js server, not serverless functions), so a detached task keeps running
// after the POST returns. Results are held briefly so a poll can read them, then
// swept. Nothing here is durable: a restart mid-inspection simply loses the job,
// and the viewer shows its "stopped before it finished" message.

const RESULT_TTL_MS = 5 * 60 * 1000;
const ENGINE_TIMEOUT_MS = (() => {
  const raw = Number(process.env.MSW_ENGINE_INSPECT_TIMEOUT_MS);
  return Number.isFinite(raw) && raw > 0 ? raw : 600_000;
})();

export type InspectStatus = "pending" | "ok" | "error";

export interface InspectJob {
  id: string;
  status: InspectStatus;
  /** JSON model from the engine once status is "ok". */
  payload?: unknown;
  /** Human-readable failure once status is "error". */
  error?: string;
  /** Distinct HTTP-ish status for the error, so the client can word it precisely. */
  errorStatus?: number;
  startedAt: number;
  updatedAt: number;
}

const jobs = new Map<string, InspectJob>();

function sweep(): void {
  const cutoff = Date.now() - RESULT_TTL_MS;
  for (const [id, job] of jobs) {
    if (job.updatedAt < cutoff) jobs.delete(id);
  }
}

function createId(): string {
  return globalThis.crypto.randomUUID();
}

/**
 * Registers an inspection job and runs the engine call detached.
 *
 * The browser's upload has already been read into `body` before this is called,
 * so the POST can answer with the job id the moment the bytes have arrived and
 * the engine work continues in the background. This is what lets the viewer work
 * from any network: the request the browser is waiting on is only as long as the
 * upload, never the upload plus the decompile.
 */
export function startInspect(body: Buffer, contentType: string, engineBase: string, label: string): InspectJob {
  sweep();
  const job: InspectJob = {
    id: createId(),
    status: "pending",
    startedAt: Date.now(),
    updatedAt: Date.now(),
  };
  jobs.set(job.id, job);

  const headers: Record<string, string> = {};
  const token = engineToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  const url = `${engineBase.replace(/\/+$/, "")}/api/inspect`;

  // Detached on purpose: the POST returns before this settles.
  void runInspect(job, body, contentType, url, headers, label).catch((error) => {
    job.status = "error";
    job.error = `Inspection failed unexpectedly: ${error instanceof Error ? error.message : "unknown error"}.`;
    job.errorStatus = 500;
    job.updatedAt = Date.now();
  });

  return job;
}

async function runInspect(
  job: InspectJob,
  body: Buffer,
  contentType: string,
  url: string,
  headers: Record<string, string>,
  label: string,
): Promise<void> {
  const started = Date.now();
  try {
    const response = await postEngineRaw(url, body, contentType, {
      timeoutMs: ENGINE_TIMEOUT_MS,
      headers,
    });
    const elapsed = Date.now() - started;
    if (!response.ok) {
      console.error(`[inspect] engine returned ${response.status} in ${elapsed}ms for ${label}`);
      job.status = "error";
      job.error =
        (response.json as { error?: string } | null)?.error ??
        `The engine could not open that project (${response.status}).`;
      job.errorStatus = response.status;
    } else {
      console.info(`[inspect] engine ok in ${elapsed}ms for ${label}`);
      job.status = "ok";
      job.payload = response.json;
    }
  } catch (error) {
    const elapsed = Date.now() - started;
    const aborted = error instanceof Error && error.name === "TimeoutError";
    console.error(
      `[inspect] engine failed after ${elapsed}ms for ${label}:`,
      error instanceof Error ? `${error.name}: ${error.message}` : error,
    );
    job.status = "error";
    job.errorStatus = aborted ? 504 : 502;
    job.error = aborted
      ? `Reading this project took longer than ${Math.round(ENGINE_TIMEOUT_MS / 1000)}s, so the engine call timed out. Retry, or inspect a smaller export.`
      : `The engine could not be reached: ${error instanceof Error ? error.message : "unknown error"}.`;
  } finally {
    job.updatedAt = Date.now();
  }
}

export function getInspect(id: string): InspectJob | null {
  return jobs.get(id) ?? null;
}
