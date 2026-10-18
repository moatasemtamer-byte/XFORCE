"use client";

import { useEffect, useMemo, useState } from "react";
import { ConvertStations } from "@/components/nx/convert-stations";
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

// Used until the real CPU of the target PLC is known: the engine needs some order number to
// build an openable .ap16 hardware configuration, and a default keeps the form submittable.
const DEFAULT_CPU_ORDER_NUMBER = "6ES7 515-2AM01-0AB0/V2.0";

const FILE_INPUT_CLASS =
  "w-full text-xs text-nx-body file:mr-3 file:border file:border-nx-line-strong file:bg-nx-white file:px-3 file:py-1.5 file:text-xs file:font-semibold file:text-nx-ink hover:file:bg-nx-green-50";

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

export function EngineConvertForm() {
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

  const fileSourceKind = useMemo(() => sourceKindForFile(file?.name), [file]);
  const [pickedSource, setPickedSource] = useState<SourceKind | null>(null);
  // The uploaded file decides the input; before that, a route picked from the matrix does.
  const sourceKind: SourceKind = file ? fileSourceKind : (pickedSource ?? "unknown");
  const allowedTargets = TARGETS_BY_SOURCE[sourceKind];
  // Fall back to the first legal output instead of mutating state, so an XML input
  // (which cannot yield XML) never leaves an impossible target selected.
  const target: TargetKind = allowedTargets.includes(targetChoice) ? targetChoice : allowedTargets[0];
  const tiaTarget = target === "tia";
  const xmlTarget = target === "xml";
  const projectNeedsCpu = tiaTarget && buildProject && cpu.trim().length === 0;

  // A ladder input converts to logic on its own; the DPT tag dictionary and the template
  // library are both optional refinements.
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
      .then((response) => response.json())
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

  // Tick an elapsed timer while a run is in flight (indeterminate progress).
  useEffect(() => {
    if (!busy) return;
    const startedAt = Date.now();
    const id = window.setInterval(() => setElapsedMs(Date.now() - startedAt), 250);
    return () => window.clearInterval(id);
  }, [busy]);

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
    <div className="space-y-6">
      <NxPanel>
        <NxPanelTitle
          title="Conversion routes"
          description="What you can feed in, and what each input can become. Pick a chip to set the output."
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
          Pick any route to begin — including{" "}
          <span className="font-semibold text-nx-ink">XML {"\u2192"} PT2X</span>,{" "}
          <span className="font-semibold text-nx-ink">XML {"\u2192"} AP</span>, or{" "}
          <span className="font-semibold text-nx-ink">AP {"\u2192"} PT2X</span> — then upload the matching
          file below. Once a file is chosen, only its row stays selectable.
        </p>
      </NxPanel>

      <ConvertStations active={busy} />

      <NxPanel>
        <NxPanelTitle
          title="Convert"
          description="Upload a source, choose an output, and run it."
        />
        <form onSubmit={onSubmit}>
          <NxPanelBody className="space-y-4">
            <NxField
              label="Source file"
              hint="Ladder logic PDF (.pdf), TRISTATION listing (.lpt), a TriStation XML export (.xml), or a Siemens TIA export (.zip / .xml)."
            >
              <div className="flex items-center gap-3">
                <span
                  className={nx(
                    "grid h-9 w-9 shrink-0 place-items-center border",
                    file ? "border-nx-green-700 text-nx-green-700" : "border-nx-line-strong text-nx-hint",
                  )}
                >
                  <SourceGlyph kind={sourceKind} />
                </span>
                <input
                  type="file"
                  accept=".xml,application/xml,text/xml,.pdf,.lpt,.txt,.zip,.ap16,.ap17,.ap18,.ap19,.ap20"
                  onChange={(event) => setFile(event.target.files?.[0] ?? null)}
                  className={FILE_INPUT_CLASS}
                />
              </div>
            </NxField>

            {ladderInput ? (
              <div className="grid gap-4 sm:grid-cols-2">
                <NxField
                  label="DPT dictionary"
                  hint="Optional. Maps tag names to types and hardware; the logic converts without it."
                >
                  <input
                    type="file"
                    accept=".dpt,.DPT"
                    onChange={(event) => setDptFile(event.target.files?.[0] ?? null)}
                    className={FILE_INPUT_CLASS}
                  />
                </NxField>
                <NxField
                  label="Template library"
                  hint="Optional safety template set; the engine's blank set is used when omitted."
                >
                  <input
                    type="file"
                    accept=".xml,.PT2,.pt2"
                    onChange={(event) => setTemplateFile(event.target.files?.[0] ?? null)}
                    className={FILE_INPUT_CLASS}
                  />
                </NxField>
              </div>
            ) : null}

            <fieldset className="space-y-2">
              <legend className="text-[11px] font-semibold uppercase tracking-[0.12em] text-nx-hint">
                Output format
              </legend>
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
                        "flex items-start gap-3 border p-3 text-left transition-colors",
                        selected
                          ? "border-nx-green-700 bg-nx-green-50"
                          : enabled
                            ? "border-nx-line-strong bg-nx-white hover:border-nx-green-700"
                            : "cursor-not-allowed border-nx-line bg-nx-paper opacity-60",
                      )}
                    >
                      <span
                        className={nx(
                          "grid h-9 w-9 shrink-0 place-items-center border",
                          selected
                            ? "border-nx-green-700 bg-nx-ink text-white"
                            : "border-nx-line-strong text-nx-body",
                        )}
                      >
                        <TargetGlyph kind={format.kind} />
                      </span>
                      <span className="min-w-0">
                        <span className="flex items-center gap-2">
                          <span className="text-xs font-semibold text-nx-ink">{format.short}</span>
                          <span className="font-mono text-[10px] text-nx-hint">{format.file}</span>
                        </span>
                        <span className="mt-0.5 block text-[11px] text-nx-hint">
                          {enabled ? format.detail : disabledReason(sourceKind, format.kind)}
                        </span>
                      </span>
                    </button>
                  );
                })}
              </div>
            </fieldset>

            {tiaTarget ? (
              <div className="grid gap-4 sm:grid-cols-2">
                <NxField label="TIA version" hint="Import scripts pin this on every regeneration.">
                  <select
                    value={tiaVersion}
                    onChange={(event) => setTiaVersion(Number(event.target.value))}
                    className={nxInputClass}
                  >
                    {TIA_VERSIONS.map((version) => (
                      <option key={version} value={version}>
                        TIA Portal V{version}
                      </option>
                    ))}
                  </select>
                </NxField>
                <NxField label="CPU order number" hint="Pre-filled with a stand-in; set the real CPU for a faithful hardware configuration.">
                  <input
                    value={cpu}
                    onChange={(event) => setCpu(event.target.value)}
                    className={nxInputClass}
                    placeholder={DEFAULT_CPU_ORDER_NUMBER}
                  />
                </NxField>
                <NxField
                  label="Block language"
                  hint="FBD draws logic diagrams; SCL emits text. Auto keeps a block in SCL when FBD cannot be faithful."
                >
                  <select
                    value={fbdOutput}
                    onChange={(event) => setFbdOutput(event.target.value as "scl" | "fbd" | "auto")}
                    className={nxInputClass}
                  >
                    <option value="fbd">FBD (function block diagram)</option>
                    <option value="auto">Auto (FBD, SCL fallback)</option>
                    <option value="scl">SCL (structured text)</option>
                  </select>
                </NxField>
                <NxField
                  label="FBD box dialect"
                  hint="Match the TIA build that will import the project: V16.0 GA or V16 Update 3."
                >
                  <select
                    value={fbdDialect}
                    onChange={(event) => setFbdDialect(event.target.value as "v16-ga" | "v16-update3")}
                    className={nxInputClass}
                  >
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
                  onChange={(event) => setBuildProject(event.target.checked)}
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
            ) : (
              <p className="border border-nx-line bg-nx-green-50/50 px-3 py-3 text-xs text-nx-hint">
                {xmlTarget
                  ? "Stops at the interchange step and returns the TriStation PLCopen XML — the exact file the PT2X and AP paths consume."
                  : "Builds a TriStation PT2X container with pt2conv against the embedded base project. No TIA Portal is involved."}
              </p>
            )}

            <NxField label="Notes" hint="Stored with the evidence, not used by the converter.">
              <input value={notes} onChange={(event) => setNotes(event.target.value)} className={nxInputClass} />
            </NxField>
          </NxPanelBody>

          <div className="flex flex-wrap items-center gap-4 border-t border-nx-line px-5 py-4">
            <NxButton type="submit" disabled={submitBlocked} className="min-w-52 justify-center">
              {busy ? (
                <>
                  <span className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-white/40 border-t-white" />
                  Converting…
                </>
              ) : (
                <>Convert to {targetLabel(target)}</>
              )}
            </NxButton>
            <span className="text-xs text-nx-hint">
              {projectNeedsCpu
                ? "A CPU order number is required to build a project."
                : sourceKind === "unknown" && file
                  ? "Unrecognized extension — treated as a TriStation listing."
                  : `Ready: ${file ? sourceLabel(sourceKind) : "choose a source"} → ${targetLabel(target)}.`}
            </span>
          </div>
        </form>
      </NxPanel>

      <NxPanel className={nx(result && !busy ? "nx-flash-once" : undefined)}>
        <NxPanelTitle
          title="Pipeline"
          description={
            pipelineStatus === "busy"
              ? `Converting ${file ? file.name : ""} — the whole path is live until the engine answers.`
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

          {pipelineStatus === "done" ? (
            <div className="nx-pop flex items-center gap-3 border border-nx-green-700 bg-nx-green-50 px-4 py-3">
              <span className="nx-badge-in grid h-8 w-8 shrink-0 place-items-center divide-nx-green-700 border border-nx-green-700 bg-nx-neon text-nx-ink">
                <CheckGlyph />
              </span>
              <div>
                <p className="text-xs font-semibold text-nx-ink">Conversion complete</p>
                <p className="text-[11px] text-nx-hint">
                  {file ? sourceLabel(sourceKind) : "Source"} → {targetLabel(target)}
                  {lastDurationMs !== null ? ` in ${formatDuration(lastDurationMs)}` : ""}. Downloads below.
                </p>
              </div>
            </div>
          ) : null}

          {pipelineStatus === "error" && error ? (
            <div className="border border-nx-fail-fg/40 bg-nx-fail-bg px-4 py-3 text-xs text-nx-fail-fg">
              <p>{error}</p>
              {errorLog ? (
                <>
                  <pre className="mt-2 max-h-72 overflow-auto whitespace-pre-wrap border border-nx-fail-fg/20 bg-nx-white px-3 py-2 font-mono text-[11px] text-nx-body">
                    {errorLog}
                  </pre>
                  <button
                    type="button"
                    onClick={() => downloadText("engine_report.txt", errorLog)}
                    className="mt-2 underline"
                  >
                    Download engine report
                  </button>
                </>
              ) : null}
            </div>
          ) : null}
        </NxPanelBody>
      </NxPanel>

      {result ? (
        <NxPanel>
          <NxPanelTitle
            title="Result"
            description={
              result.projectName
                ? `Project ${result.projectName} built${result.engineVersion ? ` — engine ${result.engineVersion}` : ""}`
                : result.engineVersion
                  ? `Engine ${result.engineVersion}`
                  : "Conversion finished."
            }
          />
          <NxPanelBody className="space-y-4">
            <div className="flex flex-wrap gap-3">
              {(result.artifacts ?? []).map((artifact) => (
                <NxButton
                  key={artifact.name}
                  variant={artifact.kind === "primary" ? "primary" : "quiet"}
                  onClick={() => download(artifact)}
                >
                  Download {artifact.name}
                </NxButton>
              ))}
            </div>

            {result.summary && Object.keys(result.summary).length > 0 ? (
              <dl className="grid grid-cols-2 gap-x-8 gap-y-2 text-xs sm:grid-cols-3">
                {Object.entries(result.summary).map(([key, value]) => (
                  <div key={key}>
                    <dt className="text-nx-hint">{key}</dt>
                    <dd className="mt-0.5 tabular-nums text-nx-ink">{String(value)}</dd>
                  </div>
                ))}
              </dl>
            ) : null}

            {result.warnings && result.warnings.length > 0 ? (
              <div>
                <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-nx-unv-fg">
                  Warnings ({result.warnings.length})
                </p>
                <ul className="mt-2 space-y-1.5">
                  {result.warnings.map((warning, index) => (
                    <li key={index} className="text-xs text-nx-body">
                      {warning}
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
          </NxPanelBody>
        </NxPanel>
      ) : null}
    </div>
  );
}
