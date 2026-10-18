import { STATUS_LABELS, type EvidenceStatus } from "@/lib/engine";
import { ContactIcon, contactStateFor } from "./contact-icons";

const TONE: Record<EvidenceStatus, string> = {
  verifiedInTia: "bg-nx-verified text-nx-verified-ink",
  unitTestOnly: "bg-nx-ut-bg text-nx-ut-fg",
  unverified: "bg-nx-unv-bg text-nx-unv-fg",
  notStarted: "bg-nx-ns-bg text-nx-ns-fg",
  failed: "bg-nx-fail-bg text-nx-fail-fg",
};

export function StatusChip({
  status,
  className = "",
}: {
  status: EvidenceStatus;
  className?: string;
}) {
  return (
    <span
      className={`inline-flex items-center gap-1.5 whitespace-nowrap px-2 py-1 text-[10px] font-semibold uppercase tracking-[0.06em] ${TONE[status]} ${className}`}
    >
      <ContactIcon state={contactStateFor(status)} className="h-3 w-3" />
      {STATUS_LABELS[status]}
    </span>
  );
}