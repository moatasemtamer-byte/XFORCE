import type { EvidenceStatus } from "@/lib/engine";

/**
 * Ladder contact states. The status word is always shown next to the icon, so
 * the icon reinforces the meaning instead of replacing it.
 */
export type ContactState = "closed" | "half" | "open" | "broken";

export function contactStateFor(status: EvidenceStatus): ContactState {
  switch (status) {
    case "verifiedInTia":
      return "closed";
    case "unitTestOnly":
    case "unverified":
      return "half";
    case "failed":
      return "broken";
    default:
      return "open";
  }
}

export function ContactIcon({
  state,
  className = "",
}: {
  state: ContactState;
  className?: string;
}) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.5}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className={className || "h-4 w-4"}
    >
      {/* leads and terminals, common to every state */}
      <path d="M2 12h3.5M18.5 12H22" />
      <circle cx="5.6" cy="12" r="1.1" fill="currentColor" stroke="none" />
      <circle cx="18.4" cy="12" r="1.1" fill="currentColor" stroke="none" />

      {state === "closed" ? <path d="M6.7 12h10.6" /> : null}
      {state === "half" ? (
        <>
          <path d="M6.7 12h5" />
          <path d="M12.6 12h2" strokeDasharray="1.4 1.8" />
        </>
      ) : null}
      {state === "open" ? <path d="M6.7 12l8.2-5" /> : null}
      {state === "broken" ? (
        <>
          <path d="M6.7 12h3.6" />
          <path d="M13.7 12h3.4" />
          <path d="M11.6 10l2 4M13.6 10l-2 4" />
        </>
      ) : null}
    </svg>
  );
}