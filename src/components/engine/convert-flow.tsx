"use client";

import { Fragment, type ReactNode } from "react";
import { nx } from "@/components/nx/primitives";

export type SourceKind = "pdf" | "lpt" | "xml" | "ap" | "unknown";
export type TargetKind = "xml" | "pt2x" | "tia";

export const SOURCE_FORMATS: Array<{
  kind: Exclude<SourceKind, "unknown">;
  label: string;
  short: string;
  detail: string;
}> = [
  { kind: "pdf", label: "Ladder logic PDF", short: "PDF", detail: "Printed ladder logic (.pdf)" },
  { kind: "lpt", label: "TRISTATION listing", short: "LPT", detail: "Text logic export (.lpt)" },
  { kind: "xml", label: "TriStation XML", short: "XML", detail: "PLCopen export (.xml)" },
  { kind: "ap", label: "Siemens TIA export", short: "AP", detail: "SimaticML export (.zip/.xml)" },
];

export const TARGET_FORMATS: Array<{
  kind: TargetKind;
  label: string;
  short: string;
  detail: string;
  file: string;
}> = [
  { kind: "xml", label: "TriStation XML", short: "XML", detail: "Intermediate PLCopen XML", file: ".xml" },
  { kind: "pt2x", label: "TriStation PT2X", short: "PT2X", detail: "Loadable TriStation container", file: ".pt2x" },
  { kind: "tia", label: "TIA Portal (AP)", short: "AP", detail: "Siemens import package / project", file: ".zip" },
];

/** Which outputs each input can produce. An XML input is already XML, so it only routes onward. */
export const TARGETS_BY_SOURCE: Record<SourceKind, TargetKind[]> = {
  pdf: ["xml", "pt2x", "tia"],
  lpt: ["xml", "pt2x", "tia"],
  xml: ["pt2x", "tia"],
  ap: ["xml", "pt2x"],
  unknown: ["xml", "pt2x", "tia"],
};

/** Extensions that identify a Siemens SimaticML / TIA Openness export. An export is a
 *  .zip of Blocks/Tags/UDTs or a single block .xml; the engine sniffs the contents. */
const AP_EXTENSIONS = new Set(["ap16", "ap17", "ap18", "ap19", "ap20", "zip"]);

export function sourceKindForFile(name: string | undefined | null): SourceKind {
  if (!name || !name.includes(".")) return "unknown";
  const ext = name.split(".").pop()!.toLowerCase();
  if (ext === "pdf") return "pdf";
  if (ext === "lpt" || ext === "txt") return "lpt";
  if (ext === "xml") return "xml";
  if (AP_EXTENSIONS.has(ext)) return "ap";
  return "unknown";
}

export function sourceLabel(kind: SourceKind): string {
  return SOURCE_FORMATS.find((format) => format.kind === kind)?.label ?? "Source";
}

export function targetLabel(kind: TargetKind): string {
  return TARGET_FORMATS.find((format) => format.kind === kind)?.label ?? "Output";
}

export function disabledReason(source: SourceKind, target: TargetKind): string | null {
  if (TARGETS_BY_SOURCE[source].includes(target)) return null;
  if (source === "xml" && target === "xml") return "Input is already XML";
  return "Not available for this input";
}

export function SourceGlyph({ kind, className = "nx-icon" }: { kind: SourceKind; className?: string }) {
  return (
    <svg viewBox="0 0 24 24" className={className} fill="none" stroke="currentColor" strokeWidth={1.5} aria-hidden="true">
      {kind === "pdf" ? (
        <>
          <path d="M7 3h7l4 4v14H7z" />
          <path d="M14 3v4h4" />
          <path d="M9.5 13h5M9.5 16h5" strokeLinecap="round" />
        </>
      ) : null}
      {kind === "lpt" ? (
        <>
          <rect x="5" y="4" width="14" height="16" />
          <path d="M8 8.5h8M8 12h8M8 15.5h5" strokeLinecap="round" />
        </>
      ) : null}
      {kind === "xml" ? (
        <>
          <path d="M9 7 5 12l4 5" strokeLinecap="round" strokeLinejoin="round" />
          <path d="m15 7 4 5-4 5" strokeLinecap="round" strokeLinejoin="round" />
        </>
      ) : null}
      {kind === "ap" ? (
        <>
          <rect x="4" y="4" width="16" height="16" />
          <path d="M4 10h16M10 4v16" />
        </>
      ) : null}
      {kind === "unknown" ? (
        <>
          <rect x="5" y="4" width="14" height="16" />
          <path d="M9 10h6M9 14h6" strokeLinecap="round" />
        </>
      ) : null}
    </svg>
  );
}

export function TargetGlyph({ kind, className = "nx-icon" }: { kind: TargetKind; className?: string }) {
  return (
    <svg viewBox="0 0 24 24" className={className} fill="none" stroke="currentColor" strokeWidth={1.5} aria-hidden="true">
      {kind === "xml" ? (
        <>
          <path d="M9 7 5 12l4 5" strokeLinecap="round" strokeLinejoin="round" />
          <path d="m15 7 4 5-4 5" strokeLinecap="round" strokeLinejoin="round" />
        </>
      ) : null}
      {kind === "pt2x" ? (
        <>
          <path d="m12 3 8 4.5v9L12 21l-8-4.5v-9z" strokeLinejoin="round" />
          <path d="m4 7.5 8 4.5 8-4.5M12 12v9" />
        </>
      ) : null}
      {kind === "tia" ? (
        <>
          <rect x="4" y="4" width="16" height="16" />
          <path d="M4 10h16M10 4v16" />
        </>
      ) : null}
    </svg>
  );
}

export function CheckGlyph({ className = "nx-icon" }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" className={className} fill="none" stroke="currentColor" strokeWidth={2.5} aria-hidden="true">
      <path className="nx-check-path" d="m5 13 4 4 10-11" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

/**
 * The full input -> output matrix, always visible. Every legal route is drawn; the row for
 * the chosen/uploaded input is highlighted. Before a file is picked every row is selectable
 * (so "XML -> PT2X" is reachable on its own); once a file is uploaded only its row is.
 */
export function ConvertRouteMap({
  sourceKind,
  target,
  onSelectRoute,
  locked = false,
  hasFile = false,
}: {
  sourceKind: SourceKind;
  target: TargetKind;
  onSelectRoute: (source: Exclude<SourceKind, "unknown">, target: TargetKind) => void;
  locked?: boolean;
  hasFile?: boolean;
}) {
  return (
    <div className="divide-y divide-nx-line">
      {SOURCE_FORMATS.map((source) => {
        const active = source.kind === sourceKind;
        const allowed = TARGETS_BY_SOURCE[source.kind];
        return (
          <div
            key={source.kind}
            className={nx(
              "flex flex-col gap-3 px-5 py-3.5 transition-colors sm:flex-row sm:items-center sm:gap-4",
              active ? "bg-nx-green-50/70" : "bg-nx-white",
            )}
          >
            <div className="flex items-center gap-2.5 sm:w-60">
              <span
                className={nx(
                  "grid h-8 w-8 shrink-0 place-items-center border",
                  active ? "border-nx-green-700 text-nx-green-700" : "border-nx-line-strong text-nx-hint",
                )}
              >
                <SourceGlyph kind={source.kind} />
              </span>
              <span className="min-w-0">
                <span className="block text-xs font-semibold text-nx-ink">{source.label}</span>
                <span className="block text-[11px] text-nx-hint">{source.detail}</span>
              </span>
            </div>

            <span
              aria-hidden="true"
              className={nx(
                "hidden shrink-0 font-mono text-sm sm:block",
                active ? "text-nx-green-700" : "text-nx-hint/60",
              )}
            >
              {"\u2192"}
            </span>

            <div className="flex flex-wrap items-center gap-1.5">
              {TARGET_FORMATS.map((format) => {
                const enabled = allowed.includes(format.kind);
                const selected = active && target === format.kind;
                const clickable = enabled && !locked && (active || !hasFile);
                return (
                  <button
                    key={format.kind}
                    type="button"
                    disabled={!clickable}
                    onClick={() => onSelectRoute(source.kind, format.kind)}
                    title={
                      !enabled
                        ? (disabledReason(source.kind, format.kind) ?? "Not available")
                        : clickable
                          ? `Convert ${source.short} \u2192 ${format.short}`
                          : `${source.short} \u2192 ${format.short} (upload a ${source.detail.match(/\(([^)]+)\)/)?.[1] ?? source.short} file)`
                    }
                    className={nx(
                      "inline-flex items-center gap-1.5 border px-2.5 py-1.5 text-[11px] font-semibold transition-colors",
                      selected
                        ? "border-nx-ink bg-nx-ink text-white"
                        : clickable
                          ? "border-nx-line-strong bg-nx-white text-nx-ink hover:border-nx-green-700 hover:bg-nx-green-50"
                          : enabled
                            ? "cursor-default border-nx-line bg-nx-white text-nx-hint"
                            : "cursor-not-allowed border-nx-line bg-nx-paper text-nx-hint/50 line-through",
                    )}
                  >
                    <TargetGlyph kind={format.kind} className="h-3.5 w-3.5" />
                    {format.short}
                  </button>
                );
              })}
            </div>
          </div>
        );
      })}
    </div>
  );
}

type PipelineStatus = "idle" | "busy" | "done" | "error";

interface PipelineNode {
  key: string;
  label: string;
  glyph: ReactNode;
}

export function pipelineNodes(sourceKind: SourceKind, target: TargetKind, sourceName?: string): PipelineNode[] {
  const nodes: PipelineNode[] = [
    { key: "read", label: sourceName ?? "Source", glyph: <SourceGlyph kind={sourceKind} /> },
  ];
  if (sourceKind !== "xml") {
    nodes.push({ key: "xml", label: "XML", glyph: <TargetGlyph kind="xml" /> });
  }
  const finalGlyph: ReactNode =
    target === "pt2x" ? <TargetGlyph kind="pt2x" /> : target === "tia" ? <TargetGlyph kind="tia" /> : <TargetGlyph kind="xml" />;
  const finalLabel = target === "pt2x" ? "PT2X" : target === "tia" ? "AP" : "XML";
  nodes.push({ key: "out", label: finalLabel, glyph: finalGlyph });
  return nodes;
}

/** The live path: source -> (XML) -> output, drawn as wired nodes. */
export function ConvertPipeline({
  sourceKind,
  target,
  status,
  sourceName,
}: {
  sourceKind: SourceKind;
  target: TargetKind;
  status: PipelineStatus;
  sourceName?: string;
}) {
  const nodes = pipelineNodes(sourceKind, target, sourceName);

  // The whole path is live at once (one engine call, no per-stage cursor), so a busy
  // run marks every node "active"; the final node is where an error is attributed.
  function stateFor(index: number): "idle" | "active" | "done" | "error" {
    if (status === "error") return index === nodes.length - 1 ? "error" : "idle";
    if (status === "busy") return "active";
    if (status === "done") return "done";
    return "idle";
  }

  return (
    <div className="flex flex-col gap-2.5 sm:flex-row sm:items-center">
      {nodes.map((node, index) => {
        const state = stateFor(index);
        return (
          <Fragment key={node.key}>
            <div
              data-state={state}
              className="nx-node min-w-0 max-w-full"
              aria-current={state === "active" ? "step" : undefined}
            >
              <span className="shrink-0">
                {state === "done" ? <CheckGlyph /> : node.glyph}
              </span>
              <span className="truncate text-xs font-semibold">{node.label}</span>
            </div>
            {index < nodes.length - 1 ? (
              <span className="nx-wire sm:flex-1" data-live={status === "busy"} aria-hidden="true" />
            ) : null}
          </Fragment>
        );
      })}
    </div>
  );
}
