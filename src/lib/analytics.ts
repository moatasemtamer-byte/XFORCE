import { getDb } from "./db";

/** One day of conversion activity for the sparkline/bar chart. */
export interface ActivityPoint {
  /** ISO date (UTC), e.g. 2026-10-20. */
  day: string;
  /** Short label for the axis, e.g. "20 Oct". */
  label: string;
  completed: number;
  failed: number;
  total: number;
}

/** A slice of the source-format breakdown. */
export interface FormatSlice {
  /** Grouped family, e.g. "Ladder PDF". */
  family: string;
  count: number;
  share: number;
}

export interface RetentionInfo {
  retentionDays: number;
  /** ISO timestamp of when the oldest retained job falls out of the window, or null. */
  oldestJobAt: string | null;
  /** Whole days until that oldest job is purged, or null when there is nothing to expire. */
  daysUntilOldestPurge: number | null;
  /** Jobs that will be purged within the next 7 days. */
  expiringSoon: number;
  /** Total bytes currently held on disk for this account. */
  storedBytes: number;
}

export interface DashboardAnalytics {
  activity: ActivityPoint[];
  formats: FormatSlice[];
  successRate: number;
  completed: number;
  failed: number;
  total: number;
  retention: RetentionInfo;
}

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function dayKey(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function dayLabel(key: string): string {
  const [, month, day] = key.split("-").map(Number);
  return `${day} ${MONTHS[month - 1] ?? ""}`.trim();
}

/** Best-effort grouping of a source filename into a coarse family. */
function sourceFamily(name: string): string {
  const lower = name.toLowerCase();
  const ext = lower.includes(".") ? lower.slice(lower.lastIndexOf(".") + 1) : "";
  if (ext === "pdf") return "Ladder PDF";
  if (["lpt", "lst", "txt", "log", "csv"].includes(ext)) return "TRISTATION listing";
  if (ext === "xml") return "XML export";
  if (ext === "zip" || ext.startsWith("ap")) return "TIA export";
  if (ext === "pt2" || ext === "pt2x") return "TriStation project";
  return "Other";
}

/**
 * Everything the dashboard charts need, computed in one pass over the account's
 * jobs. Kept out of jobs.ts so it reads only: it issues SELECTs and never mutates.
 */
export function dashboardAnalytics(userId: string, retentionDays: number, days = 14): DashboardAnalytics {
  const db = getDb();

  const rows = db
    .prepare(
      `SELECT status, source_name, source_size, created_at
       FROM jobs WHERE user_id = ? ORDER BY created_at DESC`,
    )
    .all(userId) as Array<{ status: string; source_name: string; source_size: number; created_at: string }>;

  const total = rows.length;
  let completed = 0;
  let failed = 0;
  let storedBytes = 0;

  const buckets = new Map<string, { completed: number; failed: number }>();
  const today = new Date();
  today.setUTCHours(0, 0, 0, 0);
  for (let offset = days - 1; offset >= 0; offset -= 1) {
    const date = new Date(today);
    date.setUTCDate(today.getUTCDate() - offset);
    buckets.set(dayKey(date), { completed: 0, failed: 0 });
  }

  const formatCounts = new Map<string, number>();

  const purgeCutoff = Number.isFinite(retentionDays)
    ? new Date(Date.now() - retentionDays * 86_400_000).toISOString()
    : null;
  const soonCutoff = Number.isFinite(retentionDays)
    ? new Date(Date.now() - (retentionDays - 7) * 86_400_000).toISOString()
    : null;
  let expiringSoon = 0;
  let oldestJobAt: string | null = null;

  for (const row of rows) {
    storedBytes += row.source_size;
    if (row.status === "completed") completed += 1;
    else if (row.status === "failed") failed += 1;

    const bucket = buckets.get(row.created_at.slice(0, 10));
    if (bucket) {
      if (row.status === "completed") bucket.completed += 1;
      else if (row.status === "failed") bucket.failed += 1;
    }

    const family = sourceFamily(row.source_name);
    formatCounts.set(family, (formatCounts.get(family) ?? 0) + 1);

    if (retentionDays > 0) {
      if (!oldestJobAt || row.created_at < oldestJobAt) oldestJobAt = row.created_at;
      if (soonCutoff && row.created_at < soonCutoff && (!purgeCutoff || row.created_at >= purgeCutoff)) {
        expiringSoon += 1;
      }
    }
  }

  const activity: ActivityPoint[] = [...buckets.entries()].map(([day, value]) => ({
    day,
    label: dayLabel(day),
    completed: value.completed,
    failed: value.failed,
    total: value.completed + value.failed,
  }));

  const formats: FormatSlice[] = [...formatCounts.entries()]
    .map(([family, count]) => ({ family, count, share: total > 0 ? count / total : 0 }))
    .sort((a, b) => b.count - a.count);

  const finished = completed + failed;
  const successRate = finished > 0 ? completed / finished : 0;

  const retention: RetentionInfo = {
    retentionDays,
    oldestJobAt,
    daysUntilOldestPurge:
      oldestJobAt && Number.isFinite(retentionDays)
        ? Math.max(0, Math.ceil((new Date(oldestJobAt).getTime() + retentionDays * 86_400_000 - Date.now()) / 86_400_000))
        : null,
    expiringSoon,
    storedBytes,
  };

  return { activity, formats, successRate, completed, failed, total, retention };
}
