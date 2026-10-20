"use client";

import { useEffect, useState } from "react";
import { PtxViewer } from "@/components/engine/pt2x-viewer";

/**
 * Hands a finished job's PT2X output to the project viewer without the user
 * re-uploading it. The bytes are pulled from the job output route (which streams
 * the stored file) and stashed in sessionStorage exactly where the viewer looks,
 * so the viewer decompiles the real artifact the engine produced.
 */
export function JobViewer({ jobId, outputIndex }: { jobId: string; outputIndex: number }) {
  const [ready, setReady] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const response = await fetch(`/api/jobs/${jobId}/output/${outputIndex}`, { cache: "no-store" });
        if (!response.ok) throw new Error(`The stored file could not be loaded (${response.status}).`);
        const blob = await response.blob();
        const buffer = await blob.arrayBuffer();
        const bytes = new Uint8Array(buffer);
        let binary = "";
        const chunk = 0x8000;
        for (let i = 0; i < bytes.length; i += chunk) {
          binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
        }
        const name = `job-${jobId}.pt2x`;
        sessionStorage.setItem("msw-pt2x-view", JSON.stringify({ name, base64: btoa(binary) }));
        if (!cancelled) setReady(true);
      } catch (cause) {
        if (!cancelled) setError(cause instanceof Error ? cause.message : "The file could not be loaded.");
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [jobId, outputIndex]);

  if (error) {
    return (
      <div className="border-2 border-nx-fail-fg/50 bg-nx-fail-bg px-5 py-4 text-sm font-semibold text-nx-fail-fg">
        {error}
      </div>
    );
  }

  if (!ready) {
    return (
      <div className="flex items-center gap-3 border border-nx-line bg-nx-paper px-5 py-4 text-sm text-nx-hint">
        <span className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-nx-line-strong border-t-nx-green-700" />
        Loading the converted project…
      </div>
    );
  }

  return <PtxViewer />;
}
