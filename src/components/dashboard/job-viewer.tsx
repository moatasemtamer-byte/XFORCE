"use client";

import { useEffect, useState } from "react";
import { PtxViewer } from "@/components/engine/pt2x-viewer";

/**
 * Hands a finished job's PT2X output to the project viewer without the user
 * re-uploading it. The bytes are pulled from the job output route (which streams
 * the stored file) and passed straight to the viewer as a File: TriStation
 * containers are tens of megabytes, so staging them in sessionStorage (or any
 * other quota-bound store) throws "exceeded the quota".
 */
export function JobViewer({ jobId, outputIndex }: { jobId: string; outputIndex: number }) {
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const response = await fetch(`/api/jobs/${jobId}/output/${outputIndex}`, { cache: "no-store" });
        if (!response.ok) throw new Error(`The stored file could not be loaded (${response.status}).`);
        const blob = await response.blob();
        const name = `job-${jobId}.pt2x`;
        if (!cancelled) setFile(new File([blob], name, { type: "application/octet-stream" }));
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

  if (!file) {
    return (
      <div className="flex items-center gap-3 border border-nx-line bg-nx-paper px-5 py-4 text-sm text-nx-hint">
        <span className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-nx-line-strong border-t-nx-green-700" />
        Loading the converted project…
      </div>
    );
  }

  return <PtxViewer initialFile={file} />;
}
