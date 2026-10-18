import type { ProjectStatus } from "@/lib/engine";

function Metric({
  label,
  value,
  tone = "none",
}: {
  label: string;
  value: number;
  tone?: "none" | "warn" | "bad";
}) {
  const toneClass =
    tone === "bad" ? "text-red-300" : tone === "warn" ? "text-amber-300" : "text-white";
  return (
    <span className="inline-flex items-baseline gap-1.5">
      <span className={`font-semibold tabular-nums ${toneClass}`}>{value}</span>
      <span className="text-white/55">{label}</span>
    </span>
  );
}

export function NxFooterBand({ status, error }: { status: ProjectStatus; error: string | null }) {
  return (
    <footer className="sticky bottom-0 z-40 border-t-[3px] border-nx-neon bg-nx-ink text-white">
      <div className="flex w-full flex-wrap items-center gap-x-6 gap-y-1.5 px-6 py-2.5 text-[11px] lg:px-10">
        <span className="inline-flex items-center gap-2 font-semibold text-nx-neon">
          <svg viewBox="0 0 24 24" className="h-3.5 w-3.5" fill="none" stroke="currentColor" strokeWidth={1.8} aria-hidden="true">
            <path d="M12 3.5 21 19H3z" strokeLinejoin="round" />
            <path d="M12 10v4M12 16.6v.4" strokeLinecap="round" />
          </svg>
          Safety-critical TMR output — verify before use
        </span>

        <div className="flex flex-wrap items-center gap-x-5 gap-y-1">
          <Metric label="unverified" value={status.unverifiedCount} tone={status.unverifiedCount > 0 ? "warn" : "none"} />
          <Metric
            label="unit-test only"
            value={status.unitTestOnlyCount}
            tone={status.unitTestOnlyCount > 0 ? "warn" : "none"}
          />
          <Metric
            label="compile warnings"
            value={status.compileWarnings}
            tone={status.compileWarnings > 0 ? "warn" : "none"}
          />
          <Metric
            label="open known issues"
            value={status.openKnownIssues}
            tone={status.openKnownIssues > 0 ? "bad" : "none"}
          />
        </div>

        {error ? (
          <span className="ml-auto max-w-md truncate text-red-300" title={error}>
            {error}
          </span>
        ) : status.evidenceTimestamp ? (
          <span className="ml-auto text-white/55">Evidence as of {status.evidenceTimestamp}</span>
        ) : null}
      </div>
    </footer>
  );
}