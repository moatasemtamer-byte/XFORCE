import { Fragment } from "react";

const STATIONS = ["Parse", "Validate", "Map", "Generate", "Report"] as const;

// Engine stage labels (from /api/progress) mapped onto the station they represent. Stages the
// engine reports but no station owns (e.g. "building pt2x") simply keep the cursor where it is.
const STAGE_TO_STATION: Record<string, number> = {
  preparing: 0,
  "reading ladder": 0,
  "circuit-reader": 0,
  "ladder cli": 2,
  decompiling: 0,
  converting: 3,
  "building pt2x": 3,
  done: STATIONS.length,
};

/**
 * The stages of a conversion, drawn as terminals on a wire. When the engine reports a real
 * progress stage (from the progressId poll) the cursor advances station by station; before
 * that — or when polling is unavailable — the wire animates as a single in-flight run, which
 * is the honest fallback because the engine used to report only a final result.
 */
export function ConvertStations({ active = false, stage }: { active?: boolean; stage?: string | null }) {
  const cursor =
    stage && stage in STAGE_TO_STATION ? STAGE_TO_STATION[stage] : stage === "done" ? STATIONS.length : null;

  return (
    <div
      aria-label="Conversion stages: parse, validate, map, generate, report"
      className="flex flex-col gap-3 border border-nx-line bg-nx-white px-5 py-4 sm:flex-row sm:items-center sm:gap-2"
    >
      {STATIONS.map((station, index) => {
        const done = cursor !== null && index < cursor;
        const current = cursor !== null && index === cursor && active;
        return (
          <Fragment key={station}>
            <div className="flex items-center gap-3 sm:flex-col sm:gap-1.5">
              <span
                className={
                  done
                    ? "grid h-9 w-9 place-items-center border border-nx-green-700 bg-nx-neon text-nx-ink"
                    : current
                      ? "grid h-9 w-9 place-items-center border-2 border-nx-green-700 bg-nx-green-50 text-nx-green-700"
                      : "grid h-9 w-9 place-items-center border border-nx-line-strong bg-nx-white text-nx-ink"
                }
              >
                {done ? (
                  <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth={2.5} aria-hidden="true">
                    <path className="nx-check-path" d="m5 13 4 4 10-11" strokeLinecap="round" strokeLinejoin="round" />
                  </svg>
                ) : (
                  <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth={1.5} aria-hidden="true">
                    <rect x="5" y="6" width="14" height="12" />
                    <path d="M9 6V4M15 6V4M8.5 12h7" strokeLinecap="round" />
                  </svg>
                )}
              </span>
              <span
                className={
                  done || current
                    ? "text-[10px] font-semibold uppercase tracking-[0.12em] text-nx-ink"
                    : "text-[10px] font-semibold uppercase tracking-[0.12em] text-nx-body"
                }
              >
                {station}
              </span>
            </div>
            {index < STATIONS.length - 1 ? <span className="nx-wire sm:flex-1" data-live={active && !done} /> : null}
          </Fragment>
        );
      })}
    </div>
  );
}
