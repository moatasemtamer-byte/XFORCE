"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  CheckGlyph,
  ConvertPipeline,
  ConvertRouteMap,
  SourceGlyph,
  TargetGlyph,
  TARGET_FORMATS,
  TARGETS_BY_SOURCE,
  disabledReason,
  sourceKindForFile,
  sourceLabel,
  targetLabel,
  type SourceKind,
  type TargetKind,
} from "@/components/engine/convert-flow";
import { ConvertStations } from "@/components/nx/convert-stations";
import {
  NxButton,
  NxField,
  NxPanel,
  NxPanelBody,
  NxPanelTitle,
  nx,
  nxInputClass,
} from "@/components/nx/primitives";

interface Artifact {
  name: string;
  contentBase64?: string;
  mimeType?: string;
  kind?: string;
}

interface ConvertResult {
  artifacts?: Artifact[];
  warnings?: string[];
  summary?: Record<string, unknown>;
  engineVersion?: string;
  projectName?: string;
  error?: string;
  log?: string;
  report?: string;
  ladderReport?: string;
}

const TIA_VERSIONS = [16, 17, 18, 19, 20];
const DEFAULT_CPU_ORDER_NUMBER = "6ES7 515-2AM01-0AB0/V2.0";

const ACCEPT = ".pdf,.lpt,.txt,.xml,.zip,.ap16,.ap17,.ap18,.ap19,.ap20";

function formatDuration(ms: number): string {
  const total = Math.max(0, Math.round(ms / 1000));
  const minutes = Math.floor(total / 60);
  const seconds = total % 60;
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}

function downloadText(name: string, text: string) {
  const blob = new Blob([text], { type: "text/plain" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function download(artifact: Artifact) {
  if (!artifact.contentBase64) return;
  const bytes = Uint8Array.from(atob(artifact.contentBase64), (c) => c.charCodeAt(0));
  const blob = new Blob([bytes], { type: artifact.mimeType ?? "application/octet-stream" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = artifact.name;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

export function EngineConvertForm({ error: engineError }: { error: string | null }) {
  const [file, setFile] = useState<File | null>(null);
  const [dptFile, setDptFile] = useState<File | null>(null);
  const [templateFile, setTemplateFile] = useState<File | null>(null);
  const [targetChoice, setTargetChoice] = useState<TargetKind>("tia");
  const [tiaVersion, setTiaVersion] = useState(16);
  const [fbdOutput, setFbdOutput] = useState<"scl" | "fbd" | "auto">("fbd");
  const [fbdDialect, setFbdDialect] = useState<"v16-ga" | "v16-update3">("v16-ga");
  const [cpu, setCpu] = useState(DEFAULT_CPU_ORDER_NUMBER);
  const [notes, setNotes] = useState("");
  const [busy, setBusy] = useState(false);
  const [elapsedMs, setElapsedMs] = useState(0);
  const [lastDurationMs, setLastDurationMs] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [errorLog, setErrorLog] = useState<string | null>(null);
  const [result, setResult] = useState<ConvertResult | null>(null);
  const [buildProject, setBuildProject] = useState(false);
  const [tiaImportAvailable, setTiaImportAvailable] = useState<boolean | null>(null);
  const [dragActive, setDragActive] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const fileSourceKind = useMemo(() => sourceKindForFile(file?.name), [file]);
  const [pickedSource, setPickedSource] = useState<SourceKind | null>(null);
  const sourceKind: SourceKind = file ? fileSourceKind : (pickedSource ?? "unknown");
  const allowedTargets = TARGETS_BY_SOURCE[sourceKind];
  const target: TargetKind = allowedTargets.includes(targetChoice) ? targetChoice : allowedTargets[0];
  const tiaTarget = target === "tia";
  const xmlTarget = target === "xml";
  const projectNeedsCpu = tiaTarget && buildProject && cpu.trim().length === 0;

  const ladderInput = sourceKind === "pdf" || sourceKind === "lpt";
  const submitBlocked = !file || busy || projectNeedsCpu;

  const pipelineStatus: "idle" | "busy" | "done" | "error" = busy
    ? "busy"
    : error
      ? "error"
      : result
        ? "done"
        : "idle";

  useEffect(() => {
    let cancelled = false;
    fetch("/api/engine/health")
      .then((r) => r.json())
      .then((payload: { tiaImport?: { available?: boolean } | null }) => {
        if (!cancelled) setTiaImportAvailable(payload?.tiaImport?.available ?? false);
      })
      .catch(() => {
        if (!cancelled) setTiaImportAvailable(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!busy) return;
    const startedAt = Date.now();
    const id = window.setInterval(() => setElapsedMs(Date.now() - startedAt), 250);
    return () => window.clearInterval(id);
  }, [busy]);

  const onDrop = useCallback(
    (event: React.DragEvent) => {
      event.preventDefault();
      setDragActive(false);
      if (busy) return;
      const dropped = event.dataTransfer.files?.[0];
      if (dropped) setFile(dropped);
    },
    [busy],
  );

  const onDragOver = useCallback((event: React.DragEvent) => {
    event.preventDefault();
    setDragActive(true);
  }, []);

  const onDragLeave = useCallback(() => setDragActive(false), []);

  function resetAll() {
    setFile(null);
    setDptFile(null);
    setTemplateFile(null);
    setError(null);
    setErrorLog(null);
    setResult(null);
    setPickedSource(null);
    setLastDurationMs(null);
  }

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (!file) return;
    setBusy(true);
    setElapsedMs(0);
    setError(null);
    setErrorLog(null);
    setResult(null);
    setLastDurationMs(null);
    const startedAt = Date.now();
    try {
      const body = new FormData();
      body.set("file", file);
      body.set("target", target);
      body.set("options", JSON.stringify({ tiaVersion, cpu: cpu.trim() || DEFAULT_CPU_ORDER_NUMBER, fbdOutput, fbdDialect }));
      if (notes.trim()) body.set("notes", notes.trim());
      if (dptFile) body.set("dpt", dptFile);
      if (templateFile) body.set("template", templateFile);
      const endpoint = tiaTarget && buildProject ? "/api/engine/convert/project" : "/api/engine/convert";
      const response = await fetch(endpoint, { method: "POST", body });
      const json = (await response.json()) as ConvertResult;
      if (!response.ok) {
        setError(json.error ?? `The engine returned ${response.status}.`);
        const detail = json.report ?? json.ladderReport ?? json.log;
        if (detail) setErrorLog(detail);
        return;
      }
      setResult(json);
      setLastDurationMs(Date.now() - startedAt);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "The conversion request failed.");
    } finally {
      setBusy(false);
    }
  }

  const sourceName = file?.name;

  return (
    <div className="grid gap-6 xl:grid-cols-[1fr_380px]">
      {/* ================================================================
          PRIMARY: the conversion panel. Everything else is secondary.
          ================================================================ */}
      <div className="min-w-0 space-y-6">
        {engineError ? (
          <div className="border border-nx-fail-fg/40 bg-nx-fail-bg px-4 py-3 text-xs text-nx-fail-fg">
            {engineError}
          </div>
        ) : null}

        {/* ---- Upload zone ---- */}
        <NxPanel className={nx(dragActive && !busy ? "border-nx-green-700 bg-nx-green-50/50" : undefined)}>
          <NxPanelTitle
            title="Source file"
            description="Drag a file here, or click to browse. The engine accepts PDF, LPT, XML and Siemens TIA exports."
          />
          <NxPanelBody>
            <div
              onDrop={onDrop}
              onDragOver={onDragOver}
              onDragLeave={onDragLeave}
              onClick={() => fileInputRef.current?.click()}
              role="button"
              tabIndex={0}
              onKeyDown={(e) => {
                if (e.key === "Enter" || e.key === " ") fileInputRef.current?.click();
              }}
              className={nx(
                "flex cursor-pointer flex-col items-center justify-center gap-3 border-2 border-dashed px-6 py-10 text-center transition-colors",
                file
                  ? "border-nx-green-700 bg-nx-green-50/60"
                  : dragActive
                    ? "border-nx-green-700 bg-nx-green-50"
                    : "border-nx-line-strong bg-nx-paper hover:border-nx-green-700 hover:bg-nx-green-50/40",
                busy && "pointer-events-none opacity-60",
              )}
            >
              {file ? (
                <>
                  <span className="grid h-12 w-12 place-items-center border border-nx-green-700 bg-nx-neon text-nx-ink">
                    <SourceGlyph kind={sourceKind} className="h-6 w-6" />
                  </span>
                  <div>
                    <p className="text-sm font-semibold text-nx-ink">{file.name}</p>
                    <p className="mt-1 text-xs text-nx-hint">
                      {sourceLabel(sourceKind)} &middot; {(file.size / 1024 / 1024).toFixed(1)} MB
                    </p>
                  </div>
                  <button
                    type="button"
                    disabled={busy}
                    onClick={(e) => {
                      e.stopPropagation();
                      setFile(null);
                    }}
                    className="text-xs text-nx-hint underline hover:text-nx-ink"
                  >
                    Choose a different file
                  </button>
                </>
              ) : (
                <>
                  <span className="grid h-12 w-12 place-items-center border border-nx-line-strong bg-nx-white text-nx-hint">
                    <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth={1.5}>
                      <path d="M12 16V4m0 0l-4 4m4-4l4 4" strokeLinecap="round" strokeLinejoin="round" />
                      <path d="M4 16v2a2 2 0 002 2h12a2 2 0 002-2v-2" strokeLinecap="round" />
                    </svg>
                  </span>
                  <div>
                    <p className="text-sm font-semibold text-nx-ink">Drop your file here or click to browse</p>
                    <p className="mt-1 text-xs text-nx-hint">PDF &middot; LPT &middot; XML &middot; AP16 &middot; ZIP</p>
                  </div>
                </>
              )}
              <input
                ref={fileInputRef}
                type="file"
                accept={ACCEPT}
                className="hidden"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              />
            </div>

            {file && sourceKind === "unknown" ? (
              <p className="mt-2 text-xs text-nx-unv-fg">
                Unrecognized extension — treated as a TriStation listing (.lpt).
              </p>
            ) : null}
          </NxPanelBody>
        </NxPanel>

        {/* ---- Optional ladder extras ---- */}
        {ladderInput ? (
          <NxPanel>
            <NxPanelTitle title="Optional inputs" description="DPT tag dictionary and safety template library. Both are optional." />
            <NxPanelBody className="grid gap-4 sm:grid-cols-2">
              <NxField label="DPT dictionary" hint="Maps tag names to types and hardware. The logic converts without it.">
                <input
                  type="file"
                  accept=".dpt,.DPT"
                  onChange={(e) => setDptFile(e.target.files?.[0] ?? null)}
                  className="w-full text-xs text-nx-body file:mr-3 file:border file:border-nx-line-strong file:bg-nx-white file:px-3 file:py-1.5 file:text-xs file:font-semibold file:text-nx-ink hover:file:bg-nx-green-50"
                />
              </NxField>
              <NxField label="Template library" hint="Safety template set. The engine's blank set is used when omitted.">
                <input
                  type="file"
                  accept=".xml,.PT2,.pt2"
                  onChange={(e) => setTemplateFile(e.target.files?.[0] ?? null)}
                  className="w-full text-xs text-nx-body file:mr-3 file:border file:border-nx-line-strong file:bg-nx-white file:px-3 file:py-1.5 file:text-xs file:font-semibold file:text-nx-ink hover:file:bg-nx-green-50"
                />
              </NxField>
            </NxPanelBody>
          </NxPanel>
        ) : null}

        {/* ---- Output format ---- */}
        <NxPanel>
          <NxPanelTitle
            title="Output format"
            description="Pick what you want the engine to produce. Only legal outputs for your input are enabled."
          />
          <NxPanelBody className="space-y-3">
            <div className="grid gap-3 sm:grid-cols-3">
              {TARGET_FORMATS.map((format) => {
                const enabled = allowedTargets.includes(format.kind);
                const selected = target === format.kind;
                return (
                  <button
                    key={format.kind}
                    type="button"
                    disabled={!enabled || busy}
                    onClick={() => setTargetChoice(format.kind)}
                    className={nx(
                      "flex flex-col items-start gap-2.5 border p-4 text-left transition-colors",
                      selected
                        ? "border-nx-green-700 bg-nx-green-50 ring-1 ring-nx-green-700"
                        : enabled
                          ? "border-nx-line-strong bg-nx-white hover:border-nx-green-700 hover:bg-nx-green-50/40"
                          : "cursor-not-allowed border-nx-line bg-nx-paper opacity-50",
                    )}
                  >
                    <span
                      className={nx(
                        "grid h-10 w-10 place-items-center border",
                        selected
                          ? "border-nx-green-700 bg-nx-ink text-white"
                          : "border-nx-line-strong text-nx-body",
                      )}
                    >
                      <TargetGlyph kind={format.kind} className="h-5 w-5" />
                    </span>
                    <span>
                      <span className="flex items-center gap-2">
                        <span className="text-xs font-semibold text-nx-ink">{format.short}</span>
                        <span className="font-mono text-[10px] text-nx-hint">{format.file}</span>
                      </span>
                      <span className="mt-0.5 block text-[11px] text-nx-hint">
                        {enabled ? format.detail : disabledReason(sourceKind, format.kind)}
                      </span>
                    </span>
                    {selected ? (
                      <span className="mt-auto flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wide text-nx-green-700">
                        <CheckGlyph className="h-3 w-3" /> Selected
                      </span>
                    ) : null}
                  </button>
                );
              })}
            </div>

            {/* TIA-specific options */}
            {tiaTarget ? (
              <div className="grid gap-4 border-t border-nx-line pt-4 sm:grid-cols-2">
                <NxField label="TIA version" hint="Import scripts pin this on every regeneration.">
                  <select value={tiaVersion} onChange={(e) => setTiaVersion(Number(e.target.value))} className={nxInputClass}>
                    {TIA_VERSIONS.map((v) => (
                      <option key={v} value={v}>TIA Portal V{v}</option>
                    ))}
                  </select>
                </NxField>
                <NxField label="CPU order number" hint="Set the real CPU for a faithful hardware configuration.">
                  <input value={cpu} onChange={(e) => setCpu(e.target.value)} className={nxInputClass} placeholder={DEFAULT_CPU_ORDER_NUMBER} />
                </NxField>
                <NxField label="Block language" hint="FBD draws diagrams; SCL emits text. Auto keeps a block in SCL when FBD cannot be faithful.">
                  <select value={fbdOutput} onChange={(e) => setFbdOutput(e.target.value as "scl" | "fbd" | "auto")} className={nxInputClass}>
                    <option value="fbd">FBD (function block diagram)</option>
                    <option value="auto">Auto (FBD, SCL fallback)</option>
                    <option value="scl">SCL (structured text)</option>
                  </select>
                </NxField>
                <NxField label="FBD box dialect" hint="Match the TIA build that will import the project.">
                  <select value={fbdDialect} onChange={(e) => setFbdDialect(e.target.value as "v16-ga" | "v16-update3")} className={nxInputClass}>
                    <option value="v16-ga">V16.0 GA</option>
                    <option value="v16-update3">V16 Update 3</option>
                  </select>
                </NxField>
              </div>
            ) : null}

            {tiaTarget ? (
              <label className="flex items-start gap-3 border border-nx-line bg-nx-green-50/50 px-3 py-3">
                <input
                  type="checkbox"
                  checked={buildProject}
                  disabled={tiaImportAvailable === false}
                  onChange={(e) => setBuildProject(e.target.checked)}
                  className="mt-0.5"
                />
                <span className="text-xs text-nx-body">
                  <span className="font-semibold text-nx-ink">Build openable TIA project (.ap16)</span>
                  <span className="mt-0.5 block text-nx-hint">
                    {tiaImportAvailable === false
                      ? "TIA Portal is not installed on the engine host, so only the import package can be returned."
                      : "Runs the Openness import on the engine host and returns a zip of the TIA project. Takes several minutes."}
                  </span>
                </span>
              </label>
            ) : xmlTarget ? (
              <p className="border border-nx-line bg-nx-green-50/50 px-3 py-3 text-xs text-nx-hint">
                Stops at the interchange step and returns the TriStation PLCopen XML — the exact file the PT2X and AP paths consume.
              </p>
            ) : (
              <p className="border border-nx-line bg-nx-green-50/50 px-3 py-3 text-xs text-nx-hint">
                Builds a TriStation PT2X container with pt2conv against the embedded base project. No TIA Portal is involved.
              </p>
            )}

            <NxField label="Notes" hint="Stored with the evidence, not used by the converter.">
              <input value={notes} onChange={(e) => setNotes(e.target.value)} className={nxInputClass} />
            </NxField>
          </NxPanelBody>
        </NxPanel>

        {/* ---- Submit ---- */}
        <div className="flex flex-wrap items-center gap-4">
          <NxButton type="submit" disabled={submitBlocked} className="min-w-56 justify-center text-sm" onClick={onSubmit}>
            {busy ? (
              <>
                <span className="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white" />
                Converting… ({formatDuration(elapsedMs)})
              </>
            ) : (
              <>Convert to {targetLabel(target)}</>
            )}
          </NxButton>
          <span className="text-xs text-nx-hint">
            {projectNeedsCpu
              ? "A CPU order number is required to build a project."
              : file
                ? `${sourceLabel(sourceKind)} → ${targetLabel(target)}`
                : "Choose a source file to begin."}
          </span>
        </div>

        {/* ---- Pipeline animation ---- */}
        <NxPanel className={nx(result && !busy ? "nx-flash-once" : undefined)}>
          <NxPanelTitle
            title="Pipeline"
            description={
              pipelineStatus === "busy"
                ? `Converting ${file?.name ?? ""} — the whole path is live until the engine answers.`
                : pipelineStatus === "done"
                  ? `Finished${lastDurationMs !== null ? ` in ${formatDuration(lastDurationMs)}` : ""}.`
                  : pipelineStatus === "error"
                    ? "The run stopped before the output was produced."
                    : "The path lights up while a conversion is in flight."
            }
          />
          <NxPanelBody className="space-y-4">
            <ConvertPipeline
              sourceKind={sourceKind}
              target={target}
              status={pipelineStatus}
              sourceName={sourceName ?? (sourceKind === "unknown" ? "Source" : sourceLabel(sourceKind))}
            />

            {pipelineStatus === "busy" ? (
              <div className="space-y-2">
                <div className="nx-progress" role="progressbar" aria-label="Conversion in progress" />
                <div className="flex items-center justify-between text-[11px] text-nx-hint">
                  <span className="inline-flex items-center gap-1.5">
                    <span className="inline-block h-1.5 w-1.5 animate-pulse bg-nx-green-700" />
                    Working — this can take a few minutes.
                  </span>
                  <span className="tabular-nums">{formatDuration(elapsedMs)}</span>
                </div>
              </div>
            ) : null}
          </NxPanelBody>
        </NxPanel>

        {/* ---- Completion banner (obvious) ---- */}
        {pipelineStatus === "done" && result ? (
          <div className="nx-pop border-2 border-nx-green-700 bg-nx-green-50 p-6">
            <div className="flex items-start gap-4">
              <span className="nx-badge-in grid h-14 w-14 shrink-0 place-items-center border-2 border-nx-green-700 bg-nx-neon text-nx-ink">
                <CheckGlyph className="h-7 w-7" />
              </span>
              <div className="min-w-0 flex-1">
                <p className="text-base font-bold text-nx-ink">Conversion complete</p>
                <p className="mt-1 text-xs text-nx-hint">
                  {file ? sourceLabel(sourceKind) : "Source"} → {targetLabel(target)}
                  {lastDurationMs !== null ? ` in ${formatDuration(lastDurationMs)}` : ""}
                  {result.projectName ? ` · Project ${result.projectName}` : ""}
                  {result.engineVersion ? ` · Engine ${result.engineVersion}` : ""}
                </p>
                <div className="mt-4 flex flex-wrap gap-2">
                  {(result.artifacts ?? []).map((artifact) => (
                    <NxButton
                      key={artifact.name}
                      variant={artifact.kind === "primary" ? "primary" : "quiet"}
                      onClick={() => download(artifact)}
                      className="text-xs"
                    >
                      <svg viewBox="0 0 24 24" className="h-3.5 w-3.5" fill="none" stroke="currentColor" strokeWidth={2}>
                        <path d="M12 4v12m0 0l-4-4m4 4l4-4M4 18h16" strokeLinecap="round" strokeLinejoin="round" />
                      </svg>
                      {artifact.name}
                    </NxButton>
                  ))}
                </div>
                {result.summary && Object.keys(result.summary).length > 0 ? (
                  <dl className="mt-4 grid grid-cols-2 gap-x-6 gap-y-1.5 text-xs sm:grid-cols-3">
                    {Object.entries(result.summary).map(([key, value]) => (
                      <div key={key} className="flex items-baseline justify-between gap-2 sm:block">
                        <dt className="text-nx-hint">{key}</dt>
                        <dd className="tabular-nums text-nx-ink sm:mt-0.5">{String(value)}</dd>
                      </div>
                    ))}
                  </dl>
                ) : null}
                {result.warnings && result.warnings.length > 0 ? (
                  <div className="mt-3">
                    <p className="text-[11px] font-semibold uppercase tracking-wide text-nx-unv-fg">
                      Warnings ({result.warnings.length})
                    </p>
                    <ul className="mt-1 space-y-1">
                      {result.warnings.map((w, i) => (
                        <li key={i} className="text-xs text-nx-body">{w}</li>
                      ))}
                    </ul>
                  </div>
                ) : null}
              </div>
            </div>
            <div className="mt-4 flex gap-3 border-t border-nx-green-700/30 pt-3">
              <button type="button" onClick={resetAll} className="text-xs font-semibold text-nx-green-700 underline hover:text-nx-ink">
                Convert another file
              </button>
              <Link href="/engine" className="text-xs font-semibold text-nx-hint underline hover:text-nx-ink">
                View dashboard
              </Link>
            </div>
          </div>
        ) : null}

        {/* ---- Failure banner (obvious, with way back) ---- */}
        {pipelineStatus === "error" && error ? (
          <div className="border-2 border-nx-fail-fg/50 bg-nx-fail-bg p-6">
            <div className="flex items-start gap-4">
              <span className="grid h-12 w-12 shrink-0 place-items-center border-2 border-nx-fail-fg/40 bg-nx-white text-nx-fail-fg">
                <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth={2}>
                  <path d="M6 6l12 12M18 6L6 18" strokeLinecap="round" />
                </svg>
              </span>
              <div className="min-w-0 flex-1">
                <p className="text-base font-bold text-nx-fail-fg">Conversion failed</p>
                <p className="mt-1 text-xs text-nx-fail-fg">{error}</p>
                {errorLog ? (
                  <details className="mt-3">
                    <summary className="cursor-pointer text-xs font-semibold text-nx-fail-fg underline">Engine report</summary>
                    <pre className="mt-2 max-h-72 overflow-auto whitespace-pre-wrap border border-nx-fail-fg/20 bg-nx-white px-3 py-2 font-mono text-[11px] text-nx-body">
                      {errorLog}
                    </pre>
                    <button
                      type="button"
                      onClick={() => downloadText("engine_report.txt", errorLog)}
                      className="mt-2 text-xs underline"
                    >
                      Download engine report
                    </button>
                  </details>
                ) : null}
              </div>
            </div>
            <div className="mt-4 flex flex-wrap gap-3 border-t border-nx-fail-fg/20 pt-3">
              <button type="button" onClick={resetAll} className="text-xs font-semibold text-nx-fail-fg underline hover:text-nx-ink">
                Try again
              </button>
              <Link href="/" className="text-xs font-semibold text-nx-hint underline hover:text-nx-ink">
                Back to home
              </Link>
              <Link href="/engine" className="text-xs font-semibold text-nx-hint underline hover:text-nx-ink">
                Engine dashboard
              </Link>
            </div>
          </div>
        ) : null}

        {/* ---- Result panel (secondary detail) ---- */}
        {result && pipelineStatus === "done" ? null : result ? (
          <NxPanel>
            <NxPanelTitle title="Result" description={result.engineVersion ? `Engine ${result.engineVersion}` : undefined} />
            <NxPanelBody className="space-y-4">
              <div className="flex flex-wrap gap-3">
                {(result.artifacts ?? []).map((a) => (
                  <NxButton key={a.name} variant={a.kind === "primary" ? "primary" : "quiet"} onClick={() => download(a)}>
                    Download {a.name}
                  </NxButton>
                ))}
              </div>
            </NxPanelBody>
          </NxPanel>
        ) : null}
      </div>

      {/* ================================================================
          SECONDARY: routes map + stations. Sticky, always visible.
          ================================================================ */}
      <div className="min-w-0 space-y-6 xl:sticky xl:top-16 xl:self-start">
        <NxPanel>
          <NxPanelTitle
            title="Conversion routes"
            description="What each input can become. Click a chip to set the output."
          />
          <ConvertRouteMap
            sourceKind={sourceKind}
            target={target}
            hasFile={!!file}
            onSelectRoute={(source, format) => {
              setPickedSource(source);
              setTargetChoice(format);
            }}
            locked={busy}
          />
          <p className="border-t border-nx-line bg-nx-paper px-5 py-2.5 text-[11px] text-nx-hint">
            Pick any route to begin — then upload the matching file on the left. Once a file is chosen, only its row stays selectable.
          </p>
        </NxPanel>

        <ConvertStations active={busy} />
      </div>
    </div>
  );
}
