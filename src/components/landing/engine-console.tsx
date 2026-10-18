import { LadderStrip } from "./ladder";
import { MicroLabel } from "./primitives";

/**
 * Instrument view of the XForce Engine.
 *
 * Every value shown here is real: the limits are the configured limits, and
 * the session and artefact names are the ones the engine actually produces.
 * Nothing about accuracy, speed or output volume is implied, because those
 * depend on the source document.
 */
const LOG = [
  { kind: "input", text: "ingest — reading source (text-based ladder PDF)" },
  { kind: "step", text: "detect — source interpreted as ladder logic" },
  { kind: "step", text: "reconstruct — 4 networks, branch detected" },
  { kind: "step", text: "generate — 7 Boolean equations derived" },
  { kind: "step", text: "cross-reference — 7 references resolved" },
  { kind: "step", text: "analyse — 1 read without a detected writer" },
  { kind: "done", text: "export — writing artefacts" },
] as const;

const ARTIFACTS: { ext: string; note: string; primary?: boolean }[] = [
  { ext: ".equations.json", note: "Boolean logic", primary: true },
  { ext: ".report.md", note: "Analysis report" },
  { ext: ".rungs.csv", note: "Rung index" },
  { ext: ".workbook.xlsx", note: "Tabular workbook" },
  { ext: ".report.html", note: "Readable output" },
  { ext: ".tristation.tsv", note: "TRISTATION" },
];

const LIMITS: { label: string; value: string }[] = [
  { label: "Max upload", value: "32 MB" },
  { label: "Max pages", value: "400" },
  { label: "Time limit", value: "100 s" },
];

export function EngineConsole() {
  return (
    <div className="overflow-hidden border border-ink/15 bg-ink text-mist-200 shadow-[0_30px_70px_-45px_rgba(10,12,10,0.8)]">
      {/* title bar */}
      <div className="flex items-center justify-between gap-4 border-b border-white/10 px-4 py-3">
        <div className="flex items-center gap-2.5">
          <span aria-hidden="true" className="flex gap-1.5">
            <span className="h-2 w-2 rounded-full bg-white/20" />
            <span className="h-2 w-2 rounded-full bg-white/20" />
            <span className="h-2 w-2 rounded-full bg-brand-500" />
          </span>
          <span className="font-mono text-[11px] uppercase tracking-[0.18em] text-mist-300">xforce-engine</span>
        </div>
        <span className="flex items-center gap-2 font-mono text-[10px] uppercase tracking-[0.18em] text-mist-400">
          <span aria-hidden="true" className="relative flex h-1.5 w-1.5">
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-brand-500 opacity-75" />
            <span className="relative inline-flex h-1.5 w-1.5 rounded-full bg-brand-500" />
          </span>
          online
        </span>
      </div>

      <div className="grid gap-px bg-white/10 lg:grid-cols-[1.35fr_1fr]">
        {/* log */}
        <div className="bg-ink px-4 py-5 sm:px-6">
          <MicroLabel tone="ink">Session</MicroLabel>
          <ol className="mt-4 space-y-2.5 font-mono text-[11.5px] leading-relaxed sm:text-xs">
            {LOG.map((line, i) => (
              <li key={line.text} className="flex gap-2.5" style={{ animation: "rise-in .6s both", animationDelay: `${i * 90}ms` }}>
                <span aria-hidden="true" className={line.kind === "done" ? "text-brand-500" : "text-mist-400"}>
                  {line.kind === "done" ? ">" : "·"}
                </span>
                <span className={line.kind === "done" ? "text-white" : "text-mist-300"}>{line.text}</span>
              </li>
            ))}
            <li className="flex gap-2.5 text-mist-300">
              <span aria-hidden="true" className="text-brand-500">
                &gt;
              </span>
              <span>
                awaiting source
                <span aria-hidden="true" className="caret ml-0.5 inline-block text-brand-500">
                  _
                </span>
              </span>
            </li>
          </ol>

          <div className="mt-6 border-t border-white/10 pt-5">
            <LadderStrip />
          </div>
        </div>

        {/* artefacts */}
        <div className="bg-ink px-4 py-5 sm:px-6">
          <MicroLabel tone="ink">Artefacts</MicroLabel>
          <ul className="mt-4 space-y-2">
            {ARTIFACTS.map((file, i) => (
              <li
                key={file.ext}
                className="flex items-baseline justify-between gap-3 border-b border-white/[0.07] pb-2 font-mono text-[11.5px]"
                style={{ animation: "rise-in .6s both", animationDelay: `${140 + i * 80}ms` }}
              >
                <span className={file.primary ? "text-brand-500" : "text-mist-200"}>{file.ext}</span>
                <span className="text-right text-[10px] uppercase tracking-[0.12em] text-mist-400">{file.note}</span>
              </li>
            ))}
          </ul>

          <div className="mt-6">
            <MicroLabel tone="ink">Configured limits</MicroLabel>
            <dl className="mt-3 grid grid-cols-3 gap-2">
              {LIMITS.map((limit) => (
                <div key={limit.label} className="border border-white/10 px-2.5 py-2.5">
                  <dt className="font-mono text-[9.5px] uppercase tracking-[0.14em] text-mist-400">{limit.label}</dt>
                  <dd className="mt-1 font-mono text-[13px] tabular-nums text-white">{limit.value}</dd>
                </div>
              ))}
            </dl>
          </div>
        </div>
      </div>

      <p className="border-t border-white/10 bg-ink px-4 py-3 font-mono text-[10px] leading-relaxed text-mist-400 sm:px-6">
        Text-based ladder PDFs and ASCII listings are processed. Scanned PDFs require OCR. TRISTATION / MSW output
        applies to Triconex sources; other outputs are dialect-dependent.
      </p>
    </div>
  );
}
