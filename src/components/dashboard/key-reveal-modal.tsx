"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui";

/**
 * One-time key reveal. Rendered when a license was just minted; the key is only
 * shown here until dismissed. Copy puts it on the clipboard, and the close link
 * strips the `issued` param so a refresh does not bring the modal back.
 */
export function KeyRevealModal({
  keyValue,
  plan,
  ownerLabel,
  closeHref,
}: {
  keyValue: string;
  plan: string;
  ownerLabel: string | null;
  closeHref: string;
}) {
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) return;
    const id = window.setTimeout(() => setCopied(false), 2000);
    return () => window.clearTimeout(id);
  }, [copied]);

  async function copy() {
    try {
      await navigator.clipboard.writeText(keyValue);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="License key issued"
      className="fixed inset-0 z-50 flex items-center justify-center bg-ink-950/80 px-4"
    >
      <div className="w-full max-w-md rounded-xl border border-signal-400/40 bg-ink-900 p-6 shadow-2xl">
        <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-signal-300">
          License issued
        </p>
        <h2 className="mt-2 text-lg font-semibold text-mist-50">Copy this key now</h2>
        <p className="mt-1.5 text-xs leading-relaxed text-mist-400">
          This is the only time the full key is shown. Store it somewhere safe and send it to{" "}
          {ownerLabel ? <span className="text-mist-200">{ownerLabel}</span> : "the user"} — you can
          always see it again here later, but treat it like a password.
        </p>

        <div className="mt-4 flex items-stretch gap-2">
          <code className="flex-1 select-all overflow-x-auto rounded-lg border border-ink-700 bg-ink-950 px-3 py-2.5 font-mono text-sm text-signal-200">
            {keyValue}
          </code>
          <Button type="button" variant="outline" size="sm" onClick={copy}>
            {copied ? "Copied" : "Copy"}
          </Button>
        </div>

        <dl className="mt-4 grid grid-cols-2 gap-3 border-t border-ink-800 pt-4 text-[11px]">
          <div>
            <dt className="text-mist-500">Plan</dt>
            <dd className="mt-0.5 text-mist-200">{plan}</dd>
          </div>
          <div>
            <dt className="text-mist-500">Assigned to</dt>
            <dd className="mt-0.5 truncate text-mist-200">{ownerLabel ?? "Unassigned"}</dd>
          </div>
        </dl>

        <div className="mt-5 flex justify-end gap-2">
          <Button type="button" variant="ghost" size="sm" onClick={copy}>
            {copied ? "Copied" : "Copy key"}
          </Button>
          <a
            href={closeHref}
            className="inline-flex h-8 items-center justify-center rounded-md bg-signal-400 px-4 text-xs font-semibold text-ink-950 transition-colors hover:bg-signal-300"
          >
            Done
          </a>
        </div>
      </div>
    </div>
  );
}
