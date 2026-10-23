"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { NxButton, NxPanel, NxPanelBody, NxPanelTitle, NxStat, nxInputClass } from "@/components/nx/primitives";
import { FbdSheet, type InspectProject, type InspectPou, type SheetZoom } from "@/components/engine/fbd-sheet";
import { takeViewerHandoff } from "@/lib/viewer-handoff";

// ---------------------------------------------------------------------------
// POU detail: interface table, ST source, FBD sheets.
// ---------------------------------------------------------------------------

const POU_LABEL: Record<string, string> = {
  function: "Function",
  program: "Program",
  functionBlock: "Function block",
};

const SECTION_ORDER = ["input", "output", "inOut", "local"];

const GZIP_UPLOAD_THRESHOLD = 256 * 1024;

// Phases the viewer moves through for one file, shown to the user while the
// server is busy. "uploading" reports a real percentage; the others are states.
type Phase = "idle" | "compressing" | "uploading" | "reading" | "viewing" | "error";

// The kind of printable PDF the engine should build: the FBD sheets, the TriStation
// report suite (POU Variables, Tagnames Complete Listing, Project Implementation, …),
// or both.
type PdfContent = "fbd" | "reports" | "both";

const PDF_CONTENT_LABEL: Record<PdfContent, string> = {
  fbd: "FBD sheets",
  reports: "Report suite",
  both: "FBD + reports",
};

const PHASE_LABEL: Record<Exclude<Phase, "idle" | "error">, string> = {
  compressing: "Preparing the file…",
  uploading: "Uploading",
  reading: "Reading the project…",
  viewing: "Building the sheets…",
};

/**
 * Gzips a project before upload when the browser supports streaming compression.
 * TriStation containers compress ~15:1, so this is the difference between a ~40 s
 * upload over a slow link and a couple of seconds. Returns the original file
 * unchanged for small files, unsupported browsers, or when compression does not
 * actually shrink the payload.
 */
async function gzipForUpload(file: File): Promise<Blob> {
  if (file.size < GZIP_UPLOAD_THRESHOLD || typeof CompressionStream === "undefined") return file;
  try {
    const compressed = await new Response(
      file.stream().pipeThrough(new CompressionStream("gzip")),
    ).blob();
    return compressed.size < file.size ? compressed : file;
  } catch {
    return file;
  }
}

/**
 * POSTs the file to the inspect route while reporting upload progress. The browser
 * does not expose send progress on `fetch`, so this uses XMLHttpRequest, whose
 * `upload.onprogress` fires as bytes leave. Resolves with the parsed job id, or
 * rejects with the server's error message.
 */
function uploadForInspect(
  url: string,
  body: FormData,
  onProgress: (fraction: number) => void,
): Promise<{ id?: string; error?: string }> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", url);
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable && event.total > 0) onProgress(event.loaded / event.total);
    };
    xhr.onload = () => {
      let parsed: { id?: string; error?: string } = {};
      try {
        parsed = JSON.parse(xhr.responseText) as { id?: string; error?: string };
      } catch {
        parsed = {};
      }
      if (xhr.status >= 200 && xhr.status < 300) resolve(parsed);
      else reject(new Error(parsed.error ?? `The server returned ${xhr.status}.`));
    };
    xhr.onerror = () => reject(new Error("The upload connection failed."));
    xhr.onabort = () => reject(new Error("The upload was cancelled."));
    xhr.send(body);
  });
}

function PouDetail({
  pou,
  projectName,
  zoom,
  onZoomChange,
}: {
  pou: InspectPou;
  projectName: string;
  zoom: SheetZoom;
  onZoomChange: (zoom: SheetZoom) => void;
}) {
  const [sheetIndex, setSheetIndex] = useState(0);
  const sheet = pou.sheets[Math.min(sheetIndex, Math.max(0, pou.sheets.length - 1))];
  const sheetCount = pou.sheets.length;

  useEffect(() => {
    if (sheetCount < 2) return;
    const onKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      if (target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.isContentEditable)) return;
      if (event.key === "ArrowLeft") setSheetIndex((i) => Math.max(0, i - 1));
      else if (event.key === "ArrowRight") setSheetIndex((i) => Math.min(sheetCount - 1, i + 1));
      else return;
      event.preventDefault();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [sheetCount]);

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <p className="text-sm font-bold text-nx-ink">{pou.name}</p>
        <span className="text-xs font-semibold uppercase tracking-wide text-nx-hint">
          {POU_LABEL[pou.pouType] ?? pou.pouType}
          {pou.returnType ? ` → ${pou.returnType}` : ""}
        </span>
        {pou.title ? <span className="text-xs text-nx-hint">{pou.title}</span> : null}
        {sheetCount > 1 ? (
          <div className="ml-auto flex items-center gap-2">
            <button
              type="button"
              onClick={() => setSheetIndex((i) => Math.max(0, i - 1))}
              disabled={sheetIndex <= 0}
              aria-label="Previous sheet"
              title="Previous sheet (←)"
              className="flex h-7 w-7 items-center justify-center border border-nx-line text-base font-bold leading-none text-nx-ink transition-colors hover:bg-nx-green-50 disabled:cursor-not-allowed disabled:opacity-40"
            >
              ‹
            </button>
            <span className="min-w-[11rem] text-center text-xs">
              <span className="font-semibold text-nx-ink">
                Sheet {sheet.number} / {sheetCount}
              </span>
              {sheet.title ? <span className="ml-2 text-nx-hint">{sheet.title}</span> : null}
            </span>
            <button
              type="button"
              onClick={() => setSheetIndex((i) => Math.min(sheetCount - 1, i + 1))}
              disabled={sheetIndex >= sheetCount - 1}
              aria-label="Next sheet"
              title="Next sheet (→)"
              className="flex h-7 w-7 items-center justify-center border border-nx-line text-base font-bold leading-none text-nx-ink transition-colors hover:bg-nx-green-50 disabled:cursor-not-allowed disabled:opacity-40"
            >
              ›
            </button>
          </div>
        ) : sheet.title ? (
          <span className="ml-auto text-xs text-nx-hint">{sheet.title}</span>
        ) : null}
      </div>

      {pou.sheets.length > 0 ? (
        <FbdSheet
          sheet={sheet}
          drawing={pou.drawing}
          pouName={pou.name}
          pouTitle={pou.title}
          projectName={projectName}
          sheetCount={pou.sheets.length}
          zoom={zoom}
          onZoomChange={onZoomChange}
        />
      ) : (
        <pre className="max-h-[70vh] overflow-auto border border-nx-line bg-nx-white p-3 text-[11px] leading-relaxed text-nx-body">
          {pou.st ?? "This program has no FBD sheets."}
        </pre>
      )}
    </div>
  );
}

// The interface variables, referenced globals and ST source of a POU. Rendered
// in the side panel, categorised, so the FBD canvas keeps the full width above.
function PouInterface({ pou }: { pou: InspectPou }) {
  const vars = useMemo(() => {
    const sorted = [...pou.vars].sort(
      (a, b) => SECTION_ORDER.indexOf(a.section) - SECTION_ORDER.indexOf(b.section) || a.name.localeCompare(b.name),
    );
    return sorted;
  }, [pou]);

  return (
    <div className="space-y-4">
      {vars.length > 0 ? (
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-wide text-nx-hint">Interface ({vars.length})</p>
          <table className="mt-1 w-full border-collapse text-xs">
            <thead>
              <tr className="border-b border-nx-line text-left text-[10px] uppercase tracking-wide text-nx-hint">
                <th className="py-1 pr-3 font-semibold">Name</th>
                <th className="py-1 pr-3 font-semibold">Type</th>
                <th className="py-1 pr-3 font-semibold">Section</th>
                <th className="py-1 pr-3 font-semibold">Init</th>
                <th className="py-1 font-semibold">Description</th>
              </tr>
            </thead>
            <tbody>
              {vars.map((v) => (
                <tr key={`${v.section}-${v.name}`} className="border-b border-nx-line/50">
                  <td className="py-1 pr-3 font-mono text-[11px] text-nx-ink">{v.name}</td>
                  <td className="py-1 pr-3 text-nx-body">{v.dataType}</td>
                  <td className="py-1 pr-3 text-nx-hint">{v.section}</td>
                  <td className="py-1 pr-3 font-mono text-[11px] text-nx-body">{v.init || "—"}</td>
                  <td className="py-1 text-nx-hint">{v.desc || ""}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}

      {pou.referencedGlobals.length > 0 ? (
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-wide text-nx-hint">Referenced globals</p>
          <p className="mt-1 text-xs text-nx-hint">{pou.referencedGlobals.join(", ")}</p>
        </div>
      ) : null}

      {pou.st ? (
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-wide text-nx-hint">Structured text</p>
          <pre className="mt-1 max-h-[60vh] overflow-auto border border-nx-line bg-nx-white p-3 text-[11px] leading-relaxed text-nx-body">
            {pou.st}
          </pre>
        </div>
      ) : null}

      {vars.length === 0 && !pou.st && pou.referencedGlobals.length === 0 ? (
        <p className="text-xs text-nx-hint">No interface variables or source for this program.</p>
      ) : null}
    </div>
  );
}

// ---------------------------------------------------------------------------
// The viewer: drop a .pt2/.pt2x, browse POUs, tags, and logic.
// ---------------------------------------------------------------------------

export function PtxViewer({ initialFile }: { initialFile?: File | null }) {
  const router = useRouter();
  const inputRef = useRef<HTMLInputElement>(null);
  // Set when the viewer unmounts (or a new inspection starts) so a poll loop that
  // is mid-flight stops touching state for a job nobody is watching anymore.
  const cancelledRef = useRef(false);
  // The first file handed in by a parent (a finished job's output) is inspected
  // once; later renders must not start the inspection again.
  const startedFileRef = useRef<File | null>(null);
  const [project, setProject] = useState<InspectProject | null>(null);
  const [sourceName, setSourceName] = useState<string>("");
  const [pouName, setPouName] = useState<string | null>(null);
  const [phase, setPhase] = useState<Phase>("idle");
  const [uploadPct, setUploadPct] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [dragging, setDragging] = useState(false);
  const [tagFilter, setTagFilter] = useState("");
  const [pouFilter, setPouFilter] = useState("");
  const [tab, setTab] = useState<"interface" | "tags">("interface");
  const [zoom, setZoom] = useState<SheetZoom>("fit");
  // FBD sheets vs the printable PDF of the same project.
  const [viewMode, setViewMode] = useState<"fbd" | "pdf">("fbd");
  // What the printable PDF contains: the FBD sheets, the report suite, or both.
  const [pdfContent, setPdfContent] = useState<PdfContent>("fbd");
  const [pdfUrl, setPdfUrl] = useState<string | null>(null);
  const [pdfPhase, setPdfPhase] = useState<"idle" | "rendering" | "ready" | "error">("idle");
  const [pdfError, setPdfError] = useState<string | null>(null);
  // Keep the uploaded file so the PDF can be generated on demand without a re-upload.
  const [sourceFile, setSourceFile] = useState<File | null>(null);
  const pdfUrlRef = useRef<string | null>(null);
  // Which content the cached blob URL belongs to, so switching content re-renders.
  const pdfContentRef = useRef<PdfContent | null>(null);

  const clearPdf = useCallback(() => {
    if (pdfUrlRef.current) {
      URL.revokeObjectURL(pdfUrlRef.current);
      pdfUrlRef.current = null;
    }
    pdfContentRef.current = null;
    setPdfUrl(null);
    setPdfPhase("idle");
    setPdfError(null);
  }, []);

  // Renders the current project to PDF via the engine, caching the blob URL so the
  // toggle is instant after the first switch. Re-renders when the content selection
  // changes (FBD sheets / report suite / both). Runs once per project + selection.
  const renderPdf = useCallback(async (content: PdfContent) => {
    if (!sourceFile || pdfPhase === "rendering") return;
    if (pdfUrlRef.current && pdfContentRef.current === content) return;
    setPdfError(null);
    setPdfPhase("rendering");
    try {
      const payload = await gzipForUpload(sourceFile);
      const body = new FormData();
      body.append("file", payload, `${sourceFile.name}.gz`);
      const response = await fetch(
        `/api/engine/pdf?name=${encodeURIComponent(sourceFile.name)}&mode=${content}`,
        {
          method: "POST",
          body,
        },
      );
      if (!response.ok) {
        let message = `The server returned ${response.status}.`;
        try {
          const parsed = (await response.json()) as { error?: string };
          if (parsed.error) message = parsed.error;
        } catch {
          // non-JSON error body
        }
        setPdfError(message);
        setPdfPhase("error");
        return;
      }
      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      // Replace any previously cached blob (content may have changed).
      if (pdfUrlRef.current) URL.revokeObjectURL(pdfUrlRef.current);
      pdfUrlRef.current = url;
      pdfContentRef.current = content;
      setPdfUrl(url);
      setPdfPhase("ready");
    } catch (error) {
      setPdfError(
        `The PDF could not be generated: ${error instanceof Error ? error.message : "unknown error"}.`,
      );
      setPdfPhase("error");
    }
  }, [sourceFile, pdfPhase]);

  // Release the blob URL when the viewer goes away.
  useEffect(() => () => clearPdf(), [clearPdf]);

  // Controls are locked for every phase except idle/error.
  const busy = phase !== "idle" && phase !== "error";

  const inspect = useCallback(async (file: File) => {
    cancelledRef.current = false;
    setError(null);
    setProject(null);
    setUploadPct(0);
    setSourceFile(file);
    setViewMode("fbd");
    clearPdf();
    setPhase("compressing");
    const startedAt = Date.now();
    try {
      // A TriStation container is an OLE/CFB file whose streams sit in padded
      // sectors, so it gzips roughly 15:1. Uploading the compressed form turns a
      // slow ~40 s transfer into a couple of seconds; the engine gunzips it before
      // parsing. If the browser cannot compress, fall back to the raw bytes.
      const payload = await gzipForUpload(file);
      if (cancelledRef.current) return;

      setPhase("uploading");
      const body = new FormData();
      body.append("file", payload, `${file.name}.gz`);
      const started = await uploadForInspect(
        `/api/engine/inspect?name=${encodeURIComponent(file.name)}`,
        body,
        (fraction) => {
          if (!cancelledRef.current) setUploadPct(Math.round(fraction * 100));
        },
      );
      if (cancelledRef.current) return;

      if (typeof started.id !== "string") {
        setError(started.error ?? "The engine could not open that file.");
        setPhase("error");
        return;
      }

      // The bytes are in and the engine is decompiling; show that instead of "100%".
      setPhase("reading");

      const id = started.id;
      const deadline = startedAt + 10 * 60 * 1000;
      for (;;) {
        if (Date.now() > deadline) {
          setError("Reading this project took too long and was stopped. Retry, or inspect a smaller export.");
          setPhase("error");
          return;
        }
        // The browser tab may have been closed or the effect torn down; stop.
        if (cancelledRef.current) return;

        await new Promise((resolve) => setTimeout(resolve, 1500));
        const poll = await fetch(`/api/engine/inspect/${id}`, { cache: "no-store" });
        const state = (await poll.json().catch(() => null)) as
          | { status?: string; payload?: unknown; error?: string }
          | null;

        if (!poll.ok && poll.status === 404) {
          setError(state?.error ?? "That inspection is no longer available. Drop the project again.");
          setPhase("error");
          return;
        }
        if (!state) continue;

        if (state.status === "ok") {
          setPhase("viewing");
          const model = state.payload as InspectProject;
          setProject(model);
          setSourceName(file.name);
          const firstProgram = model.pous.find((p) => p.pouType === "program");
          setPouName(firstProgram?.name ?? null);
          setPhase("idle");
          return;
        }
        if (state.status === "error") {
          setError(state.error ?? "The engine could not open that file.");
          setPhase("error");
          return;
        }
        // status === "pending": keep polling.
      }
    } catch (error) {
      const seconds = Math.round((Date.now() - startedAt) / 1000);
      const detail = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
      setError(
        `The request to the server failed after about ${seconds}s (${detail}). This is your browser's connection to the server, not the conversion engine.`,
      );
      setPhase("error");
    }
  }, [clearPdf]);

  // A convert run hands its freshly built .pt2x over in memory (not sessionStorage,
  // whose ~5 MB quota large containers exceed); the dropzone covers a reload.
  useEffect(() => {
    const handoff = takeViewerHandoff();
    if (!handoff) return;
    let file: File | null = null;
    try {
      const binary = atob(handoff.base64);
      const bytes = new Uint8Array(binary.length);
      for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
      file = new File([bytes], handoff.name, { type: "application/octet-stream" });
    } catch {
      // A corrupt handoff is not worth an error banner; the dropzone is right there.
    }
    if (!file) return;
    const picked = file;
    // Deferred so the first setState happens outside the effect body.
    const timer = setTimeout(() => void inspect(picked), 0);
    return () => clearTimeout(timer);
  }, [inspect]);

  // A finished job's stored output is handed in as a File by the parent, so the
  // bytes never have to be staged in a storage area with a quota.
  useEffect(() => {
    if (!initialFile || startedFileRef.current === initialFile) return;
    startedFileRef.current = initialFile;
    const timer = setTimeout(() => void inspect(initialFile), 0);
    return () => clearTimeout(timer);
  }, [initialFile, inspect]);

  // Stop any poll loop still running when the viewer goes away.
  useEffect(() => {
    cancelledRef.current = false;
    return () => {
      cancelledRef.current = true;
    };
  }, []);

  const pou = useMemo(() => {
    const programs = (project?.pous ?? []).filter((p) => p.pouType === "program");
    return programs.find((p) => p.name === pouName) ?? programs[0] ?? null;
  }, [project, pouName]);

  const filteredPous = useMemo(() => {
    // The FBD browser shows programs only; function blocks are listed inside
    // their calling program's sheets, not as standalone entries.
    const list = (project?.pous ?? []).filter((p) => p.pouType === "program");
    const q = pouFilter.trim().toLowerCase();
    if (!q) return list;
    return list.filter((p) => p.name.toLowerCase().includes(q));
  }, [project, pouFilter]);

  return (
    <div className="space-y-6">
      <div>
        <p className="text-xs font-semibold uppercase tracking-wide text-nx-hint">Engine · PT2X viewer</p>
        <h1 className="mt-1 text-2xl font-bold text-nx-ink">TriStation project viewer</h1>
        <p className="mt-1 max-w-2xl text-sm text-nx-hint">
          Drop a <span className="font-mono">.pt2</span> or <span className="font-mono">.pt2x</span> container to browse its
          programs, interface variables and FBD sheets.
        </p>
      </div>

      <div
        onDragOver={(e) => {
          if (busy) return;
          e.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={(e) => {
          e.preventDefault();
          setDragging(false);
          if (busy) return;
          const file = e.dataTransfer.files[0];
          if (file) void inspect(file);
        }}
        className={`flex flex-col items-center justify-center gap-2 border-2 border-dashed px-6 py-10 text-center transition-colors ${
          dragging && !busy ? "border-nx-neon bg-nx-green-50" : "border-nx-line bg-nx-white"
        } ${busy ? "pointer-events-none opacity-60" : ""}`}
      >
        <p className="text-sm font-semibold text-nx-ink">Drop a .pt2 or .pt2x here</p>
        <p className="text-xs text-nx-hint">No conversion needed — the viewer reads TriStation containers directly.</p>
        <input
          ref={inputRef}
          type="file"
          accept=".pt2,.pt2x,application/octet-stream"
          className="hidden"
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) void inspect(file);
            e.target.value = "";
          }}
        />
        <NxButton type="button" variant="primary" className="mt-3 text-xs" disabled={busy} onClick={() => inputRef.current?.click()}>
          Choose file…
        </NxButton>
      </div>

      {busy ? (
        <div className="border border-nx-line bg-nx-green-50 px-4 py-3 text-xs text-nx-ink">
          <div className="flex items-center justify-between gap-3">
            <span className="inline-flex items-center gap-2">
              <span className="inline-block h-2 w-2 animate-pulse bg-nx-green-700" />
              {phase === "uploading" ? `${PHASE_LABEL.uploading}… ${uploadPct}%` : PHASE_LABEL[phase]}
            </span>
            {phase === "uploading" ? (
              <span className="tabular-nums font-semibold">{uploadPct}%</span>
            ) : null}
          </div>
          <div className="mt-2 h-1.5 w-full bg-nx-line/50">
            <div
              className="h-full bg-nx-green-700 transition-[width] duration-200"
              style={{ width: phase === "uploading" ? `${uploadPct}%` : "100%" }}
            />
          </div>
          <p className="mt-2 text-[11px] text-nx-hint">Controls are disabled until this finishes.</p>
        </div>
      ) : null}

      {error ? (
        <div className="border-2 border-nx-fail-fg/50 bg-nx-fail-bg px-5 py-4 text-sm font-semibold text-nx-fail-fg">
          {error}
        </div>
      ) : null}

      {project && !busy ? (
        <>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <NxStat label="Project" value={project.projectName || sourceName} />
            <NxStat label="Source file" value={sourceName} />
            <NxStat label="POUs" value={String(project.pouCount)} />
            <NxStat label="Tags" value={String(project.tagCount)} />
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <div className="inline-flex border border-nx-line">
              {(["fbd", "pdf"] as const).map((mode) => (
                <button
                  key={mode}
                  type="button"
                  onClick={() => {
                    setViewMode(mode);
                    if (mode === "pdf") void renderPdf(pdfContent);
                  }}
                  className={`px-3 py-1.5 text-xs font-semibold uppercase tracking-wide transition-colors ${
                    viewMode === mode ? "bg-nx-green-100 text-nx-ink" : "text-nx-hint hover:bg-nx-green-50"
                  }`}
                >
                  {mode === "fbd" ? "View as PT2X" : "View as PDF"}
                </button>
              ))}
            </div>
            {viewMode === "pdf" ? (
              <div className="inline-flex border border-nx-line">
                {(["fbd", "reports", "both"] as const).map((content) => (
                  <button
                    key={content}
                    type="button"
                    onClick={() => {
                      setPdfContent(content);
                      void renderPdf(content);
                    }}
                    disabled={pdfPhase === "rendering"}
                    className={`px-3 py-1.5 text-xs font-semibold tracking-wide transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${
                      pdfContent === content ? "bg-nx-green-100 text-nx-ink" : "text-nx-hint hover:bg-nx-green-50"
                    }`}
                  >
                    {PDF_CONTENT_LABEL[content]}
                  </button>
                ))}
              </div>
            ) : null}
            {viewMode === "pdf" && pdfUrl ? (
              <a
                href={pdfUrl}
                download={`${(project.projectName || sourceName || "project").replace(/\.(pt2x?|gz)$/i, "")}.pdf`}
                className="text-xs font-semibold text-nx-green-700 underline-offset-2 hover:underline"
              >
                Download {PDF_CONTENT_LABEL[pdfContent]} as PDF
              </a>
            ) : null}
          </div>

          {viewMode === "pdf" ? (
            <div className="space-y-3">
              {pdfPhase === "rendering" ? (
                <div className="flex items-center gap-2 border border-nx-line bg-nx-green-50 px-4 py-3 text-xs text-nx-ink">
                  <span className="inline-block h-2 w-2 animate-pulse bg-nx-green-700" />
                  Building the printable PDF from the project…
                </div>
              ) : null}
              {pdfPhase === "error" ? (
                <div className="border-2 border-nx-fail-fg/50 bg-nx-fail-bg px-5 py-4 text-sm font-semibold text-nx-fail-fg">
                  {pdfError}
                </div>
              ) : null}
              {pdfUrl ? (
                <object
                  data={pdfUrl}
                  type="application/pdf"
                  className="h-[80vh] w-full border border-nx-line bg-nx-white"
                  aria-label="PT2X PDF"
                >
                  <p className="p-4 text-xs text-nx-hint">
                    Your browser cannot display the PDF inline.{" "}
                    <a href={pdfUrl} className="font-semibold text-nx-green-700 underline">
                      Download it instead
                    </a>
                    .
                  </p>
                </object>
              ) : null}
            </div>
          ) : (
          <div className="grid gap-5 lg:grid-cols-[240px_1fr]">
            <div className="border border-nx-line bg-nx-white">
              <div className="border-b border-nx-line p-2">
                <input
                  type="text"
                  value={pouFilter}
                  onChange={(e) => setPouFilter(e.target.value)}
                  placeholder="Filter programs…"
                  className={`${nxInputClass} w-full`}
                />
              </div>
              <nav aria-label="Programs">
                <ul className="max-h-[70vh] divide-y divide-nx-line overflow-y-auto">
                  {filteredPous.map((p) => (
                    <li key={p.name}>
                      <button
                        type="button"
                        onClick={() => setPouName(p.name)}
                        className={`w-full px-3 py-2 text-left transition-colors ${
                          pou?.name === p.name ? "bg-nx-green-100" : "hover:bg-nx-green-50"
                        }`}
                      >
                        <span className="block truncate text-xs font-semibold text-nx-ink">{p.name}</span>
                        <span className="block truncate text-[10px] text-nx-hint">
                          {POU_LABEL[p.pouType] ?? p.pouType}
                          {p.sheets.length > 0 ? ` · FBD · ${p.sheets.length} sheet${p.sheets.length === 1 ? "" : "s"}` : ""}
                          {p.st ? " · ST" : ""}
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              </nav>
            </div>

            <div className="min-w-0 space-y-4">
              {pou ? (
                <PouDetail
                  key={pou.name}
                  pou={pou}
                  projectName={project.projectName}
                  zoom={zoom}
                  onZoomChange={setZoom}
                />
              ) : null}

              <div className="border-t border-nx-line pt-3">
                <div className="flex gap-4 border-b border-nx-line">
                  {(["interface", "tags"] as const).map((key) => (
                    <button
                      key={key}
                      type="button"
                      onClick={() => setTab(key)}
                      className={`-mb-px border-b-2 pb-2 text-xs font-semibold uppercase tracking-wide transition-colors ${
                        tab === key ? "border-nx-neon text-nx-ink" : "border-transparent text-nx-hint hover:text-nx-ink"
                      }`}
                    >
                      {key === "interface" ? "Interface" : `Tags (${project.tags.length})`}
                    </button>
                  ))}
                </div>

                <div className="pt-3">
                  {tab === "interface" ? (
                    pou ? (
                      <PouInterface key={pou.name} pou={pou} />
                    ) : null
                  ) : (
                    <div className="space-y-3">
                      <input
                        type="text"
                        value={tagFilter}
                        onChange={(e) => setTagFilter(e.target.value)}
                        placeholder="Filter tags…"
                        className={`${nxInputClass} max-w-xs`}
                      />
                      <div className="overflow-x-auto border border-nx-line bg-nx-white">
                        <table className="w-full border-collapse text-xs">
                          <thead>
                            <tr className="border-b border-nx-line text-left text-[10px] uppercase tracking-wide text-nx-hint">
                              <th className="px-3 py-1.5 font-semibold">Name</th>
                              <th className="px-3 py-1.5 font-semibold">Type</th>
                              <th className="px-3 py-1.5 font-semibold">Address</th>
                              <th className="px-3 py-1.5 font-semibold">Description</th>
                            </tr>
                          </thead>
                          <tbody>
                            {tagFilter.trim() ? (
                              project.tags
                                .filter((t) => {
                                  const q = tagFilter.trim().toLowerCase();
                                  return (
                                    t.name.toLowerCase().includes(q) ||
                                    t.type.toLowerCase().includes(q) ||
                                    t.desc.toLowerCase().includes(q) ||
                                    t.address.toLowerCase().includes(q)
                                  );
                                })
                                .slice(0, 500)
                                .map((t) => (
                                  <tr key={t.name} className="border-b border-nx-line/50">
                                    <td className="px-3 py-1.5 font-mono text-[11px] text-nx-ink">{t.name}</td>
                                    <td className="px-3 py-1.5 text-nx-body">{t.type}</td>
                                    <td className="px-3 py-1.5 font-mono text-[11px] text-nx-body">{t.address}</td>
                                    <td className="px-3 py-1.5 text-nx-hint">{t.desc}</td>
                                  </tr>
                                ))
                            ) : (
                              <tr>
                                <td colSpan={4} className="px-3 py-4 text-center text-xs text-nx-hint">
                                  {project.tags.length} tags. Type to filter.
                                </td>
                              </tr>
                            )}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  )}
                </div>
              </div>
            </div>
          </div>
          )}
        </>
      ) : null}

      {!project && !busy && !error ? (
        <NxPanel>
          <NxPanelTitle
            title="Nothing open yet"
            description="Drop a TriStation project above, or convert a file to PT2X and choose Open in viewer."
          />
          <NxPanelBody>
            <NxButton type="button" variant="quiet" className="text-xs" onClick={() => router.push("/engine/convert")}>
              Go to Convert
            </NxButton>
          </NxPanelBody>
        </NxPanel>
      ) : null}
    </div>
  );
}
