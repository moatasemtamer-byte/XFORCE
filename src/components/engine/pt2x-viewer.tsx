"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { NxButton, NxPanel, NxPanelBody, NxPanelTitle, NxStat, nxInputClass } from "@/components/nx/primitives";
import { FbdSheet, type InspectProject, type InspectPou } from "@/components/engine/fbd-sheet";

// ---------------------------------------------------------------------------
// POU detail: interface table, ST source, FBD sheets.
// ---------------------------------------------------------------------------

const POU_LABEL: Record<string, string> = {
  function: "Function",
  program: "Program",
  functionBlock: "Function block",
};

const SECTION_ORDER = ["input", "output", "inOut", "local"];

function PouDetail({ pou, projectName }: { pou: InspectPou; projectName: string }) {
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
          <p className="text-[11px] font-semibold uppercase tracking-wide text-nx-hint">Referenced globals</p>
          <p className="mt-1 text-xs text-nx-hint">{pou.referencedGlobals.join(", ")}</p>
        </div>
      ) : null}

      {pou.st ? (
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-wide text-nx-hint">Structured text</p>
          <pre className="mt-1 max-h-[70vh] overflow-auto border border-nx-line bg-nx-white p-3 text-[11px] leading-relaxed text-nx-body">
            {pou.st}
          </pre>
        </div>
      ) : null}

      {pou.sheets.length > 0 ? (
        <div className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <p className="text-[11px] font-semibold uppercase tracking-wide text-nx-hint">FBD sheets</p>
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
          <FbdSheet
            key={`${pou.name}-${sheet.number}`}
            sheet={sheet}
            drawing={pou.drawing}
            pouName={pou.name}
            pouTitle={pou.title}
            projectName={projectName}
            sheetCount={pou.sheets.length}
          />
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
  const [pouFilter, setPouFilter] = useState("");
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

  const filteredPous = useMemo(() => {
    const list = project?.pous ?? [];
    const q = pouFilter.trim().toLowerCase();
    if (!q) return list;
    return list.filter(
      (p) =>
        p.name.toLowerCase().includes(q) ||
        (POU_LABEL[p.pouType] ?? p.pouType).toLowerCase().includes(q),
    );
  }, [project, pouFilter]);

  return (
    <div className="space-y-6">
      <div>
        <p className="text-xs font-semibold uppercase tracking-wide text-nx-hint">Engine · PT2X viewer</p>
        <h1 className="mt-1 text-2xl font-bold text-nx-ink">TriStation project viewer</h1>
        <p className="mt-1 max-w-2xl text-sm text-nx-hint">
          Drop a <span className="font-mono">.pt2</span> or <span className="font-mono">.pt2x</span> container to browse its
          programs, function blocks, interface variables and FBD sheets.
        </p>
      </div>

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
        className={`flex flex-col items-center justify-center gap-2 border-2 border-dashed px-6 py-10 text-center transition-colors ${
          dragging ? "border-nx-neon bg-nx-green-50" : "border-nx-line bg-nx-white"
        }`}
      >
        <p className="text-sm font-semibold text-nx-ink">Drop a .pt2x here</p>
        <p className="text-xs text-nx-hint">or pick a file from disk</p>
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
        <div className="border border-nx-line bg-nx-green-50 px-4 py-3 text-xs text-nx-ink">
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
                  tab === key ? "border-nx-neon text-nx-ink" : "border-transparent text-nx-hint hover:text-nx-ink"
                }`}
              >
                {key === "pous" ? `Programs (${project.pous.length})` : `Tags (${project.tags.length})`}
              </button>
            ))}
          </div>

          {tab === "pous" ? (
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
                  <ul className="max-h-[62vh] divide-y divide-nx-line overflow-y-auto">
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

              <div className="min-w-0">
                {pou ? <PouDetail pou={pou} projectName={project.projectName} /> : null}
              </div>
            </div>
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
