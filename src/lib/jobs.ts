import fs from "node:fs";
import path from "node:path";
import { getDb } from "./db";
import { storageDir } from "./env";
import { getAdapter, ConversionOptions, EngineUnavailableError } from "./converter";
import type { DecodedJob, Job, JobOutput, PublicUser } from "./types";
import { newId, nowIso, safeFileName } from "./util";

export interface UploadPayload {
  filename: string;
  mimeType: string;
  bytes: Buffer;
}

export class QuotaError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "QuotaError";
  }
}

export function jobRoot(userId: string, jobId: string): string {
  return path.join(storageDir(), userId, jobId);
}

export function outputsDir(userId: string, jobId: string): string {
  return path.join(jobRoot(userId, jobId), "outputs");
}

export function decodeJob(job: Job): DecodedJob {
  const parse = <T>(raw: string, fallback: T): T => {
    try {
      return JSON.parse(raw) as T;
    } catch {
      return fallback;
    }
  };

  return {
    ...job,
    options: parse<Record<string, unknown>>(job.options, {}),
    outputs: parse<JobOutput[]>(job.outputs, []),
    warnings: parse<string[]>(job.warnings, []),
  };
}

export function listJobs(userId: string, limit = 50): DecodedJob[] {
  const rows = getDb()
    .prepare("SELECT * FROM jobs WHERE user_id = ? ORDER BY created_at DESC LIMIT ?")
    .all(userId, limit) as Job[];
  return rows.map(decodeJob);
}

export function getJobForUser(jobId: string, userId: string): DecodedJob | null {
  const row = getDb().prepare("SELECT * FROM jobs WHERE id = ? AND user_id = ?").get(jobId, userId) as
    | Job
    | undefined;
  return row ? decodeJob(row) : null;
}

export function listJobsForAdmin(userId: string, limit = 200): DecodedJob[] {
  const rows = getDb()
    .prepare("SELECT * FROM jobs WHERE user_id = ? ORDER BY created_at DESC LIMIT ?")
    .all(userId, limit) as Job[];
  return rows.map(decodeJob);
}

export function getJobForAdmin(jobId: string): DecodedJob | null {
  const row = getDb().prepare("SELECT * FROM jobs WHERE id = ?").get(jobId) as Job | undefined;
  return row ? decodeJob(row) : null;
}

export interface AdminUserSummary {
  id: string;
  email: string;
  name: string;
  company: string | null;
  role: string;
  plan: string;
  created_at: string;
  jobs: number;
  completed: number;
  failed: number;
  bytes: number;
  last_job_at: string | null;
}

/** Every account with its job rollups, for the admin console. */
export function listUserSummaries(limit = 200): AdminUserSummary[] {
  return getDb()
    .prepare(
      `SELECT users.id, users.email, users.name, users.company, users.role, users.plan, users.created_at,
              (SELECT COUNT(*) FROM jobs WHERE jobs.user_id = users.id) AS jobs,
              (SELECT COUNT(*) FROM jobs WHERE jobs.user_id = users.id AND status = 'completed') AS completed,
              (SELECT COUNT(*) FROM jobs WHERE jobs.user_id = users.id AND status = 'failed') AS failed,
              (SELECT COALESCE(SUM(source_size), 0) FROM jobs WHERE jobs.user_id = users.id) AS bytes,
              (SELECT MAX(created_at) FROM jobs WHERE jobs.user_id = users.id) AS last_job_at
       FROM users ORDER BY users.created_at DESC LIMIT ?`,
    )
    .all(limit) as AdminUserSummary[];
}

export interface AdminUserDetail extends AdminUserSummary {
  passwordless: boolean;
}

export function getUserForAdmin(userId: string): AdminUserDetail | null {
  const row = getDb()
    .prepare(
      `SELECT users.id, users.email, users.name, users.company, users.role, users.plan, users.created_at,
              users.password_hash,
              (SELECT COUNT(*) FROM jobs WHERE jobs.user_id = users.id) AS jobs,
              (SELECT COUNT(*) FROM jobs WHERE jobs.user_id = users.id AND status = 'completed') AS completed,
              (SELECT COUNT(*) FROM jobs WHERE jobs.user_id = users.id AND status = 'failed') AS failed,
              (SELECT COALESCE(SUM(source_size), 0) FROM jobs WHERE jobs.user_id = users.id) AS bytes,
              (SELECT MAX(created_at) FROM jobs WHERE jobs.user_id = users.id) AS last_job_at
       FROM users WHERE users.id = ?`,
    )
    .get(userId) as (AdminUserSummary & { password_hash: string }) | undefined;
  if (!row) return null;
  const { password_hash, ...rest } = row;
  return { ...rest, passwordless: password_hash === "passwordless" };
}

export function countJobs(userId: string): { total: number; completed: number; failed: number } {
  const row = getDb()
    .prepare(
      `SELECT COUNT(*) AS total,
              SUM(CASE WHEN status = 'completed' THEN 1 ELSE 0 END) AS completed,
              SUM(CASE WHEN status = 'failed' THEN 1 ELSE 0 END) AS failed
       FROM jobs WHERE user_id = ?`,
    )
    .get(userId) as { total: number; completed: number | null; failed: number | null };
  return { total: row.total, completed: row.completed ?? 0, failed: row.failed ?? 0 };
}

export function purgeExpiredForUser(userId: string, retentionDays: number): number {
  if (!Number.isFinite(retentionDays)) return 0;
  const cutoff = new Date(Date.now() - retentionDays * 86_400_000).toISOString();
  const stale = getDb()
    .prepare("SELECT id FROM jobs WHERE user_id = ? AND created_at < ?")
    .all(userId, cutoff) as { id: string }[];

  for (const { id } of stale) {
    fs.rmSync(jobRoot(userId, id), { recursive: true, force: true });
  }
  getDb().prepare("DELETE FROM jobs WHERE user_id = ? AND created_at < ?").run(userId, cutoff);
  return stale.length;
}

export interface CreateJobInput {
  user: PublicUser;
  licenseId: string | null;
  licenseKey: string | null;
  upload: UploadPayload;
  options: ConversionOptions;
  maxUploadBytes: number;
  monthlyConversions: number;
  usedThisMonth: number;
  retentionDays: number;
}

export async function createAndRunJob(input: CreateJobInput): Promise<DecodedJob> {
  if (input.upload.bytes.byteLength === 0) throw new QuotaError("The uploaded file is empty.");
  if (input.upload.bytes.byteLength > input.maxUploadBytes) {
    throw new QuotaError("The uploaded file exceeds the size limit for your plan.");
  }
  if (input.usedThisMonth >= input.monthlyConversions) {
    throw new QuotaError("You have reached this month's conversion limit.");
  }

  purgeExpiredForUser(input.user.id, input.retentionDays);

  const db = getDb();
  const jobId = newId("job");
  const createdAt = nowIso();
  const safeName = safeFileName(input.upload.filename);
  const root = jobRoot(input.user.id, jobId);

  fs.mkdirSync(path.join(root, "source"), { recursive: true });
  fs.mkdirSync(path.join(root, "outputs"), { recursive: true });
  const sourcePath = path.join(root, "source", safeName);
  fs.writeFileSync(sourcePath, input.upload.bytes);

  db.prepare(
    `INSERT INTO jobs (id, user_id, license_id, status, engine, source_name, source_size, source_path, options, outputs, warnings, error, progress, created_at, started_at, finished_at)
     VALUES (?, ?, ?, 'processing', ?, ?, ?, ?, ?, '[]', '[]', NULL, 10, ?, ?, NULL)`,
  ).run(
    jobId,
    input.user.id,
    input.licenseId,
    getAdapter().id,
    safeName,
    input.upload.bytes.byteLength,
    sourcePath,
    JSON.stringify(input.options),
    createdAt,
    createdAt,
  );

  try {
    const outcome = await getAdapter().convert(
      {
        jobId,
        filename: safeName,
        mimeType: input.upload.mimeType || "application/octet-stream",
        bytes: input.upload.bytes,
        licenseKey: input.licenseKey,
        userEmail: input.user.email,
      },
      input.options,
    );

    const outDir = outputsDir(input.user.id, jobId);
    const outputs: JobOutput[] = [];

    for (const artifact of outcome.artifacts) {
      const name = safeFileName(artifact.name);
      const target = path.join(outDir, name);
      fs.mkdirSync(path.dirname(target), { recursive: true });
      fs.writeFileSync(target, artifact.bytes);
      outputs.push({
        name,
        path: path.join(outDir, name),
        size: artifact.bytes.byteLength,
        mimeType: artifact.mimeType,
        kind: artifact.kind,
      });
    }

    db.prepare(
      `UPDATE jobs SET status = 'completed', progress = 100, outputs = ?, warnings = ?, finished_at = ?, error = NULL WHERE id = ?`,
    ).run(JSON.stringify(outputs), JSON.stringify(outcome.warnings), nowIso(), jobId);

    if (input.licenseId) {
      getDb()
        .prepare("UPDATE licenses SET last_used_at = ? WHERE id = ?")
        .run(nowIso(), input.licenseId);
    }
  } catch (error) {
    const message =
      error instanceof EngineUnavailableError
        ? `${error.message}`
        : `Conversion failed: ${error instanceof Error ? error.message : "unknown error"}`;

    db.prepare("UPDATE jobs SET status = 'failed', error = ?, finished_at = ? WHERE id = ?").run(
      message,
      nowIso(),
      jobId,
    );
  }

  return getJobForUser(jobId, input.user.id) as DecodedJob;
}
