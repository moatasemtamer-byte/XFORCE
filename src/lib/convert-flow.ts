// Server-safe conversion-route metadata and helpers.
//
// These are pure values and functions with no React or browser dependency, so they can be
// imported and *called* from Server Components (e.g. the job detail page). They used to live
// in "@/components/engine/convert-flow", which is a "use client" module: calling one of its
// exports from a Server Component throws "Attempted to call … from the server but … is on
// the client" at runtime. The React pieces (glyphs, route map, pipeline) stay in that client
// module, which re-exports everything here for backwards compatibility.

export type SourceKind = "pdf" | "lpt" | "xml" | "pt2" | "ap" | "unknown";
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
  { kind: "pt2", label: "TriStation PT2X", short: "PT2X", detail: "TriStation container (.pt2/.pt2x)" },
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
  pt2: ["xml", "tia"],
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
  if (ext === "pt2" || ext === "pt2x") return "pt2";
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
