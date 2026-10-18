"use client";

import { useEffect, useState } from "react";
import { NxPanel, NxPanelTitle } from "@/components/nx/primitives";

interface HistoryEntry {
  id?: string;
  at?: string;
  sourceName?: string;
  sourceKind?: string;
  target?: string;
  success?: boolean;
  durationMs?: number;
  artifactCount?: number | null;
  error?: string | null;
}

function formatWhen(at: string | undefined): string {
  if (!at) return "";
  const moment = new Date(at);
  if (Number.isNaN(moment.getTime())) return "";
  return moment.toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function formatDuration(ms: number | undefined): string {
  if (typeof ms !== "number" || !Number.isFinite(ms)) return "";
  const total = Math.max(0, Math.round(ms / 1000));
  const minutes = Math.floor(total / 60);
  const seconds = total % 60;
  return minutes > 0 ? `${minutes}m ${seconds}s` : `${seconds}s`;
}

/**
 * What the engine has converted, read from its append-only history. Only conversions made
 * through this engine (with MSW_OUTPUT_DIR configured) appear; an empty list is not an error.
 * `refreshKey` bumps whenever a conversion settles so the list reloads.
 */
export function ConvertHistory({ refreshKey }: { refreshKey?: number }) {
  const [entries, setEntries] = useState<HistoryEntry[] | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetch("/api/engine/history?limit=12")
      .then((r) => r.json())
      .then((payload: { entries?: HistoryEntry[] }) => {
        if (!cancelled) setEntries(payload.entries ?? []);
      })
      .catch(() => {
        if (!cancelled) setEntries([]);
      });
    return () => {
      cancelled = true;
    };
  }, [refreshKey]);

  return (
    <NxPanel>
      <NxPanelTitle title="Recent conversions" description="What this engine has converted, newest first." />
      <div className="divide-y divide-nx-line">
        {entries === null ? (
          <p className="px-5 py-4 text-xs text-nx-hint">Loading&hellip;</p>
        ) : entries.length === 0 ? (
          <p className="px-5 py-4 text-xs text-nx-hint">
            Nothing converted yet on this engine. Completed runs appear here.
          </p>
        ) : (
          entries.map((entry, index) => (
            <div key={entry.id ?? index} className="flex items-start gap-3 px-5 py-3">
              <span
                aria-hidden="true"
                className={
                  entry.success
                    ? "mt-0.5 grid h-4 w-4 shrink-0 place-items-center border border-nx-green-700 bg-nx-neon"
                    : "mt-0.5 grid h-4 w-4 shrink-0 place-items-center border border-nx-fail-fg bg-nx-fail-bg"
                }
              >
                {entry.success ? (
                  <svg viewBox="0 0 24 24" className="h-2.5 w-2.5 text-nx-ink" fill="none" stroke="currentColor" strokeWidth={3}>
                    <path d="m5 13 4 4 10-11" strokeLinecap="round" strokeLinejoin="round" />
                  </svg>
                ) : (
                  <svg viewBox="0 0 24 24" className="h-2.5 w-2.5 text-nx-fail-fg" fill="none" stroke="currentColor" strokeWidth={3}>
                    <path d="M6 6l12 12M18 6L6 18" strokeLinecap="round" />
                  </svg>
                )}
              </span>
              <div className="min-w-0 flex-1">
                <p className="truncate text-xs font-semibold text-nx-ink">{entry.sourceName ?? "upload"}</p>
                <p className="mt-0.5 text-[11px] text-nx-hint">
                  {(entry.target ?? "?").toUpperCase()}
                  {formatDuration(entry.durationMs) ? ` \u00b7 ${formatDuration(entry.durationMs)}` : ""}
                  {formatWhen(entry.at) ? ` \u00b7 ${formatWhen(entry.at)}` : ""}
                </p>
                {!entry.success && entry.error ? (
                  <p className="mt-0.5 truncate text-[11px] text-nx-fail-fg">{entry.error}</p>
                ) : null}
              </div>
            </div>
          ))
        )}
      </div>
    </NxPanel>
  );
}
