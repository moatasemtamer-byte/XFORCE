import type { PhaseStatus } from "@/lib/engine";
import { ContactIcon, contactStateFor } from "./contact-icons";
import { StatusChip } from "./status-chip";

/**
 * The phases ladder. A rung is energised (neon rail + closed contact) only when
 * its evidence is VERIFIED IN TIA. Every other state stays grey and open, so the
 * picture never claims more than the evidence does.
 */
export function LadderPhases({ phases }: { phases: PhaseStatus[] }) {
  return (
    <ol className="ml-6 border-l-[3px] border-nx-line py-1">
      {phases.map((phase) => {
        const live = phase.status === "verifiedInTia";
        return (
          <li
            key={phase.phase}
            data-live={live}
            className="nx-rung flex items-center gap-3 py-3 pr-4 pl-5"
          >
            <span aria-hidden="true" className="nx-rung-marker" />
            <ContactIcon
              state={contactStateFor(phase.status)}
              className={live ? "h-5 w-5 text-nx-green-700" : "h-5 w-5 text-nx-hint"}
            />
            <span className="w-4 text-right text-xs font-semibold tabular-nums text-nx-hint">
              {phase.phase}
            </span>
            <span className="min-w-0 flex-1 truncate text-sm text-nx-ink">{phase.name}</span>
            <span className="hidden text-xs tabular-nums text-nx-hint sm:inline">
              {phase.verifiedCount}/{phase.totalCount}
            </span>
            <StatusChip status={phase.status} />
          </li>
        );
      })}
    </ol>
  );
}