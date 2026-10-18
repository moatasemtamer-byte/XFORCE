import { Fragment } from "react";

const STATIONS = ["Parse", "Validate", "Map", "Generate", "Report"] as const;

/**
 * The stages of a conversion, drawn as terminals on a wire. The wire only
 * animates while a run is genuinely in flight; nothing per-stage is invented
 * because the engine reports a single final result.
 */
export function ConvertStations({ active = false }: { active?: boolean }) {
  return (
    <div
      aria-label="Conversion stages: parse, validate, map, generate, report"
      className="flex flex-col gap-3 border border-nx-line bg-nx-white px-5 py-4 sm:flex-row sm:items-center sm:gap-2"
    >
      {STATIONS.map((station, index) => (
        <Fragment key={station}>
          <div className="flex items-center gap-3 sm:flex-col sm:gap-1.5">
            <span className="grid h-9 w-9 place-items-center border border-nx-line-strong bg-nx-white text-nx-ink">
              <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth={1.5} aria-hidden="true">
                <rect x="5" y="6" width="14" height="12" />
                <path d="M9 6V4M15 6V4M8.5 12h7" strokeLinecap="round" />
              </svg>
            </span>
            <span className="text-[10px] font-semibold uppercase tracking-[0.12em] text-nx-body">
              {station}
            </span>
          </div>
          {index < STATIONS.length - 1 ? <span className="nx-wire sm:flex-1" data-live={active} /> : null}
        </Fragment>
      ))}
    </div>
  );
}