"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { NxButton, NxPanel, NxPanelBody, NxPanelTitle, NxStat, nxInputClass } from "@/components/nx/primitives";

// ---------------------------------------------------------------------------
// The JSON model pt2conv tojson emits for a TriStation .pt2/.pt2x container.
// ---------------------------------------------------------------------------

interface InspectVar {
  name: string;
  dataType: string;
  init: string;
  desc: string;
  section: string;
  isInstance: boolean;
}

interface InspectPin {
  name: string;
  wire: number;
}

interface InspectElemBase {
  id: number;
  x: number;
  y: number;
  w: number;
  h: number;
}

interface InspectBlockElem extends InspectElemBase {
  kind: "block";
  typeName: string;
  instance: string;
  inputs: InspectPin[];
  outputs: string[];
}

interface InspectVarElem extends InspectElemBase {
  kind: "var";
  name: string;
  inWire: number;
  outWire: number;
}

interface InspectCommentElem extends InspectElemBase {
  kind: "comment";
  text: string;
}

interface InspectUnknownElem extends InspectElemBase {
  kind: "unknown";
}

type InspectElem = InspectBlockElem | InspectVarElem | InspectCommentElem | InspectUnknownElem;

interface InspectWire {
  id: number;
  srcId: number;
  srcPin: string;
  sinkId: number;
  sinkPin: string;
}

interface InspectSheet {
  number: number;
  title: string;
  elems: InspectElem[];
  wires: InspectWire[];
}

interface InspectPou {
  name: string;
  pouType: string;
  returnType: string | null;
  title: string;
  vars: InspectVar[];
  referencedGlobals: string[];
  st: string | null;
  sheets: InspectSheet[];
}

interface InspectTag {
  name: string;
  type: string;
  desc: string;
  address: string;
}

interface InspectProject {
  projectName: string;
  pouCount: number;
  tagCount: number;
  pous: InspectPou[];
  tags: InspectTag[];
}

const POU_LABEL: Record<string, string> = {
  functionBlock: "Function block",
  function: "Function",
  program: "Program",
};

// ---------------------------------------------------------------------------
// FBD sheet canvas: export units × scale, blocks as boxes, wires as SVG elbows.
// ---------------------------------------------------------------------------

const SCALE = 11;

function pinY(elem: InspectElem, index: number, count: number): number {
  const slot = (index + 1) / (count + 1);
  return elem.y + elem.h * slot;
}

function elemById(elems: InspectElem[]): Map<number, InspectElem> {
  const map = new Map<number, InspectElem>();
  for (const elem of elems) map.set(elem.id, elem);
  return map;
}

function wirePoints(wire: InspectWire, byId: Map<number, InspectElem>): { sx: number; sy: number; tx: number; ty: number } | null {
  const src = byId.get(wire.srcId);
  const sink = byId.get(wire.sinkId);
  if (!src || !sink) return null;

  let sy: number;
  if (src.kind === "block") {
    const index = Math.max(0, src.outputs.indexOf(wire.srcPin));
    sy = pinY(src, index, Math.max(1, src.outputs.length));
  } else {
    sy = src.y + src.h / 2;
  }

  let ty: number;
  if (sink.kind === "block") {
    const index = Math.max(0, sink.inputs.findIndex((p) => p.name === wire.sinkPin));
    ty = pinY(sink, index, Math.max(1, sink.inputs.length));
  } else {
    ty = sink.y + sink.h / 2;
  }

  return { sx: src.x + src.w, sy, tx: sink.x, ty };
}

function FbdSheet({ sheet }: { sheet: InspectSheet }) {
  const bounds = useMemo(() => {
    let maxX = 40;
    let maxY = 20;
    for (const elem of sheet.elems) {
      maxX = Math.max(maxX, elem.x + elem.w);
      maxY = Math.max(maxY, elem.y + elem.h);
    }
    return { width: (maxX + 6) * SCALE, height: (maxY + 6) * SCALE };
  }, [sheet]);

  const byId = useMemo(() => elemById(sheet.elems), [sheet]);

  return (
    <div className="overflow-auto border border-nx-line bg-nx-white">
      <div className="relative" style={{ width: bounds.width, height: bounds.height }}>
        <svg
          aria-hidden="true"
          className="pointer-events-none absolute inset-0"
          width={bounds.width}
          height={bounds.height}
        >
          {sheet.wires.map((wire) => {
            const pts = wirePoints(wire, byId);
            if (!pts) return null;
            const sx = pts.sx * SCALE + 3;
            const sy = pts.sy * SCALE + 3;
            const tx = pts.tx * SCALE - 3;
            const ty = pts.ty * SCALE + 3;
            const mid = (sx + tx) / 2;
            return (
              <polyline
                key={wire.id}
                points={`${sx},${sy} ${mid},${sy} ${mid},${ty} ${tx},${ty}`}
                fill="none"
                stroke="#1f2937"
                strokeWidth={1.5}
              />
            );
          })}
        </svg>

        {sheet.elems.map((elem) => {
          const style = {
            left: elem.x * SCALE,
            top: elem.y * SCALE,
            width: Math.max(elem.w * SCALE, 40),
            minHeight: Math.max(elem.h * SCALE, 20),
          };

          if (elem.kind === "comment") {
            return (
              <div
                key={elem.id}
                style={style}
                className="absolute whitespace-pre-wrap border border-dashed border-nx-line-strong bg-nx-green-50 px-1.5 py-0.5 text-[10px] leading-snug text-nx-hint"
              >
                {elem.text}
              </div>
            );
          }

          if (elem.kind === "var") {
            return (
              <div
                key={elem.id}
                style={style}
                className="absolute grid place-items-center border border-nx-ink bg-nx-white px-1 text-[10px] font-semibold text-nx-ink"
                title={elem.name}
              >
                <span className="truncate">{elem.name}</span>
              </div>
            );
          }

          if (elem.kind === "block") {
            return (
              <div
                key={elem.id}
                style={style}
                className="absolute flex flex-col border-2 border-nx-ink bg-nx-white"
                title={`${elem.typeName}${elem.instance ? ` (${elem.instance})` : ""}`}
              >
                <div className="truncate border-b border-nx-line bg-nx-green-100 px-1.5 py-0.5 text-[10px] font-bold text-nx-ink">
                  {elem.typeName}
                </div>
                {elem.instance ? (
                  <div className="truncate px-1.5 py-0.5 text-[9px] text-nx-hint">{elem.instance}</div>
                ) : null}
                <div className="flex justify-between gap-1 px-1.5 pb-0.5 text-[8px] text-nx-hint">
                  <span className="truncate">{elem.inputs.map((p) => p.name).join(" ")}</span>
                  <span className="truncate">{elem.outputs.join(" ")}</span>
                </div>
              </div>
            );
          }

          return <div key={elem.id} style={style} className="absolute border border-nx-line bg-nx-white" />;
        })}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// POU detail: interface table, ST source, FBD sheets.
// ---------------------------------------------------------------------------

const SECTION_ORDER = ["input", "output", "inOut", "local"];

function PouDetail({ pou }: { pou: InspectPou }) {
  const [sheetIndex, setSheetIndex] = useState(0);
  const sheet = pou.sheets[Math.min(sheetIndex, Math.max(0, pou.sheets.length - 1))];

  const vars = useMemo(() => {
    const sorted = [...pou.vars].sort(
      (a, b) => SECTION_ORDER.indexOf(a.section) - SECTION_ORDER.indexOf(b.section) || a.name.localeCompare(b.name),
    );
    return sorted;
  }, [pou]);

  return (
    <div className="space-y-4">
      <div>
        <p className="text-sm font-bold text-nx-ink">
          {pou.name}
          <span className="ml-2 text-xs font-semibold uppercase tracking-wide text-nx-hint">
            {POU_LABEL[pou.pouType] ?? pou.pouType}
            {pou.returnType ? ` → ${pou.returnType}` : ""}
          </span>
        </p>
        {pou.title ? <p className="mt-0.5 text-xs text-nx-hint">{pou.title}</p> : null}
      </div>

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
          <p className="text-[11px] font-semibold uppercase tracking-wide text-nx-hint">
            Referenced globals ({pou.referencedGlobals.length})
          </p>
          <p className="mt-1 font-mono text-[11px] text-nx-body">{pou.referencedGlobals.join(", ")}</p>
        </div>
      ) : null}

      {pou.st ? (
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-wide text-nx-hint">Structured Text</p>
          <pre className="mt-1 max-h-96 overflow-auto border border-nx-line bg-nx-white px-3 py-2 font-mono text-[11px] leading-relaxed text-nx-body">
            {pou.st}
          </pre>
        </div>
      ) : null}

      {pou.sheets.length > 0 ? (
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <p className="text-[11px] font-semibold uppercase tracking-wide text-nx-hint">
              FBD sheets ({pou.sheets.length})
            </p>
            {pou.sheets.length > 1 ? (
              <div className="flex flex-wrap gap-1">
                {pou.sheets.map((s, i) => (
                  <button
                    key={s.number}
                    type="button"
                    onClick={() => setSheetIndex(i)}
                    className={`border px-2 py-0.5 text-[10px] font-semibold transition-colors ${
                      i === sheetIndex
                        ? "border-nx-ink bg-nx-neon text-nx-ink"
                        : "border-nx-line text-nx-hint hover:text-nx-ink"
                    }`}
                  >
                    {s.number}
                    {s.title ? ` · ${s.title}` : ""}
                  </button>
                ))}
              </div>
            ) : null}
          </div>
          <div className="mt-2">
            <FbdSheet key={`${pou.name}-${sheet.number}`} sheet={sheet} />
          </div>
          <p className="mt-1 text-[10px] text-nx-hint">
            {sheet.elems.length} elements · {sheet.wires.length} wires
          </p>
        </div>
      ) : null}
    </div>
  );
}

// ---------------------------------------------------------------------------
// The viewer: drop a .pt2/.pt2x, browse POUs, tags, and logic.
// ---------------------------------------------------------------------------

export function PtxViewer() {
  const router = useRouter();
  const inputRef = useRef<HTMLInputElement>(null);
  const [project, setProject] = useState<InspectProject | null>(null);
  const [sourceName, setSourceName] = useState<string>("");
  const [pouName, setPouName] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [dragging, setDragging] = useState(false);
  const [tagFilter, setTagFilter] = useState("");
  const [tab, setTab] = useState<"pous" | "tags">("pous");

  const inspect = useCallback(async (file: File) => {
    setBusy(true);
    setError(null);
    try {
      const body = new FormData();
      body.append("file", file, file.name);
      const response = await fetch("/api/engine/inspect", { method: "POST", body });
      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        setError(payload?.error ?? `The engine could not open that file (${response.status}).`);
        setProject(null);
        return;
      }
      const model = payload as InspectProject;
      setProject(model);
      setSourceName(file.name);
      setPouName(model.pous[0]?.name ?? null);
    } catch {
      setError("The engine could not be reached.");
      setProject(null);
    } finally {
      setBusy(false);
    }
  }, []);

  // A convert run can hand its freshly built .pt2x straight to the viewer via
  // sessionStorage (set by the convert form's "Open in viewer" button).
  useEffect(() => {
    const raw = sessionStorage.getItem("msw-pt2x-view");
    if (!raw) return;
    sessionStorage.removeItem("msw-pt2x-view");
    let file: File | null = null;
    try {
      const { name, base64 } = JSON.parse(raw) as { name: string; base64: string };
      const binary = atob(base64);
      const bytes = new Uint8Array(binary.length);
      for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
      file = new File([bytes], name, { type: "application/octet-stream" });
    } catch {
      // A corrupt stash is not worth an error banner; the dropzone is right there.
    }
    if (!file) return;
    const picked = file;
    // Deferred so the first setState happens outside the effect body.
    const timer = setTimeout(() => void inspect(picked), 0);
    return () => clearTimeout(timer);
  }, [inspect]);

  const pou = useMemo(
    () => project?.pous.find((p) => p.name === pouName) ?? project?.pous[0] ?? null,
    [project, pouName],
  );

  const filteredTags = useMemo(() => {
    if (!project) return [];
    const needle = tagFilter.trim().toLowerCase();
    if (!needle) return project.tags;
    return project.tags.filter(
      (t) =>
        t.name.toLowerCase().includes(needle) ||
        t.type.toLowerCase().includes(needle) ||
        t.address.toLowerCase().includes(needle) ||
        t.desc.toLowerCase().includes(needle),
    );
  }, [project, tagFilter]);

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-bold tracking-tight text-nx-ink">Project viewer</h1>
        <p className="mt-1 text-sm text-nx-hint">
          Open a TriStation project (.pt2/.pt2x) and inspect its POUs, interface variables, FBD logic and tag
          table in the browser — no Triconex desktop app needed.
        </p>
      </div>

      {/* Dropzone / file picker */}
      <div
        onDragOver={(e) => {
          e.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={(e) => {
          e.preventDefault();
          setDragging(false);
          const file = e.dataTransfer.files[0];
          if (file) void inspect(file);
        }}
        className={`border-2 border-dashed p-6 text-center transition-colors ${
          dragging ? "border-nx-neon bg-nx-green-50" : "border-nx-line-strong bg-nx-white"
        }`}
      >
        <p className="text-sm font-semibold text-nx-ink">Drop a .pt2 or .pt2x file here</p>
        <p className="mt-1 text-xs text-nx-hint">The container is decompiled by the engine and rendered below.</p>
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
        <NxButton type="button" variant="primary" className="mt-3 text-xs" onClick={() => inputRef.current?.click()}>
          Choose file…
        </NxButton>
      </div>

      {busy ? (
        <div className="border border-nx-line bg-nx-green-50 px-5 py-4 text-xs text-nx-ink">
          Decompiling the project container…
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

          <div className="flex gap-4 border-b border-nx-line">
            {(["pous", "tags"] as const).map((key) => (
              <button
                key={key}
                type="button"
                onClick={() => setTab(key)}
                className={`-mb-px border-b-2 pb-2 text-xs font-semibold uppercase tracking-wide transition-colors ${
                  tab === key
                    ? "border-nx-neon text-nx-ink"
                    : "border-transparent text-nx-hint hover:text-nx-ink"
                }`}
              >
                {key === "pous" ? `Programs (${project.pous.length})` : `Tags (${project.tags.length})`}
              </button>
            ))}
          </div>

          {tab === "pous" ? (
            <div className="grid gap-5 lg:grid-cols-[240px_1fr]">
              <nav aria-label="Programs" className="border border-nx-line bg-nx-white">
                <ul className="max-h-[70vh] divide-y divide-nx-line overflow-y-auto">
                  {project.pous.map((p) => (
                    <li key={p.name}>
                      <button
                        type="button"
                        onClick={() => setPouName(p.name)}
                        className={`w-full px-3 py-2 text-left transition-colors ${
                          pou?.name === p.name ? "bg-nx-green-100" : "hover:bg-nx-green-50"
                        }`}
                      >
                        <span className="block truncate text-xs font-semibold text-nx-ink">{p.name}</span>
                        <span className="mt-0.5 block text-[10px] uppercase tracking-wide text-nx-hint">
                          {POU_LABEL[p.pouType] ?? p.pouType}
                          {p.st ? " · ST" : p.sheets.length > 0 ? ` · FBD ×${p.sheets.length}` : ""}
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              </nav>

              <NxPanel>
                <NxPanelBody>
                  {pou ? <PouDetail key={pou.name} pou={pou} /> : <p className="text-xs text-nx-hint">No POUs.</p>}
                </NxPanelBody>
              </NxPanel>
            </div>
          ) : (
            <div className="space-y-3">
              <input
                type="search"
                value={tagFilter}
                onChange={(e) => setTagFilter(e.target.value)}
                placeholder="Filter by name, type, address or description…"
                className={`${nxInputClass} max-w-md`}
              />
              <div className="overflow-x-auto border border-nx-line bg-nx-white">
                <table className="w-full border-collapse text-xs">
                  <thead>
                    <tr className="border-b border-nx-line text-left text-[10px] uppercase tracking-wide text-nx-hint">
                      <th className="px-3 py-2 font-semibold">Tagname</th>
                      <th className="px-3 py-2 font-semibold">DataType</th>
                      <th className="px-3 py-2 font-semibold">Address</th>
                      <th className="px-3 py-2 font-semibold">Description</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredTags.map((t, i) => (
                      <tr key={`${t.name}-${i}`} className="border-b border-nx-line/50">
                        <td className="px-3 py-1.5 font-mono text-[11px] text-nx-ink">{t.name}</td>
                        <td className="px-3 py-1.5 text-nx-body">{t.type}</td>
                        <td className="px-3 py-1.5 font-mono text-[11px] text-nx-body">{t.address || "—"}</td>
                        <td className="px-3 py-1.5 text-nx-hint">{t.desc || ""}</td>
                      </tr>
                    ))}
                    {filteredTags.length === 0 ? (
                      <tr>
                        <td colSpan={4} className="px-3 py-4 text-center text-xs text-nx-hint">
                          No tags match.
                        </td>
                      </tr>
                    ) : null}
                  </tbody>
                </table>
              </div>
              <p className="text-[10px] text-nx-hint">
                Showing {filteredTags.length} of {project.tags.length} tags.
              </p>
            </div>
          )}
        </>
      ) : null}

      {!project && !busy && !error ? (
        <NxPanel>
          <NxPanelTitle title="Nothing open yet" description="Drop a TriStation project above, or convert a file to PT2X and choose Open in viewer." />
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
