import { EngineConsole } from "./engine-console";
import { FlowStrip } from "./ladder";
import { ActionLink, Lede, MicroLabel, Reveal, Section, SectionTitle } from "./primitives";
import { PLANS, PLAN_ORDER } from "@/lib/licensing";
import { formatBytes } from "@/lib/util";
import type { PlanId } from "@/lib/types";

/* ================================================================== *
 * 01 — The problem
 * ================================================================== */

const MANUAL_WORK = [
  "Inspect rung after rung",
  "Interpret contacts and coils",
  "Follow branches",
  "Reconstruct Boolean relationships",
  "Search for every signal reference",
  "Find where signals are written and read",
  "Compare logic between documents",
  "Prepare reports and spreadsheets by hand",
] as const;

export function ProblemSection() {
  return (
    <Section id="problem" index="01" label="The problem" tone="tint">
      <div className="pb-16 pt-10 sm:pb-24 sm:pt-14">
        <div className="grid gap-8 lg:grid-cols-[1fr_1.15fr] lg:gap-16">
          <div>
            <SectionTitle id="problem-title">
              Your control logic is structured inside the PLC — your documentation often is not.
            </SectionTitle>
          </div>
          <div className="space-y-5">
            <Lede>
              Industrial control logic is frequently delivered as PDFs, scanned drawings, printed logic sheets or
              vendor-specific text listings. The information is there. Extracting it manually is slow.
            </Lede>
            <Lede>
              For a large control system this becomes repetitive engineering work, and it is repeated again for every
              revision.
            </Lede>
          </div>
        </div>

        <div className="mt-14 grid gap-px border border-hair bg-hair lg:grid-cols-[1.15fr_1fr]">
          <Reveal className="bg-paper p-7 sm:p-9">
            <MicroLabel>What an engineer does by hand</MicroLabel>
            <ol className="mt-6 grid gap-x-8 gap-y-3 sm:grid-cols-2">
              {MANUAL_WORK.map((item, i) => (
                <li key={item} className="flex items-start gap-3 text-sm leading-relaxed text-ink-2">
                  <span aria-hidden="true" className="mt-[3px] font-mono text-[10px] tabular-nums text-brand-800">
                    {String(i + 1).padStart(2, "0")}
                  </span>
                  {item}
                </li>
              ))}
            </ol>
          </Reveal>

          <Reveal delay={120} className="flex flex-col justify-between bg-ink p-7 text-mist-200 sm:p-9">
            <div>
              <MicroLabel tone="ink">Instead</MicroLabel>
              <h3 className="mt-6 text-balance text-2xl font-semibold leading-snug text-white">
                Documentation becomes structured data.
              </h3>
              <p className="mt-4 text-sm leading-relaxed text-mist-300">
                The engine processes the source once and creates a structured representation that can be analysed,
                searched, compared and exported — instead of reading the same logic manually every time.
              </p>
            </div>
            <p className="mt-8 border-t border-white/10 pt-5 font-mono text-[10px] uppercase leading-relaxed tracking-[0.16em] text-mist-400">
              Reusable · searchable · comparable
            </p>
          </Reveal>
        </div>
      </div>
    </Section>
  );
}

/* ================================================================== *
 * 02 — The analysis pipeline
 * ================================================================== */

const PIPELINE = [
  {
    n: "01",
    title: "Ingest",
    body: "Load supported industrial logic documentation. Text-based PDF ladder documents, ASCII ladder listings, TRISTATION / Triconex listings and supported vendor-specific logic exports.",
    points: ["Text-based PDF ladder", "ASCII ladder listings", "TRISTATION / Triconex", "Supported vendor exports"],
  },
  {
    n: "02",
    title: "Detect",
    body: "Identify the applicable PLC logic dialect and apply the corresponding parsing rules, so each source family is read with the interpretation it actually needs.",
    points: ["Triconex / TRISTATION", "TI505 / TI565 SIMATIC", "Square D", "Allen-Bradley / RSLogix", "Siemens STEP 7", "IEC 61131-3 related"],
  },
  {
    n: "03",
    title: "Reconstruct",
    body: "The engine does not simply extract text — it reconstructs the logical structure, producing an internal representation of the logic instead of a collection of disconnected words.",
    points: ["Rungs and networks", "Contacts and coils", "Branches", "Timers and counters", "Latches", "Signal relationships"],
  },
  {
    n: "04",
    title: "Generate",
    body: "Reconstructed logic is expressed as structured Boolean equations that preserve the relationships the parser identified — logic that was locked inside a drawing becomes machine-readable.",
    equation: "MOTOR_RUN = START AND MOTOR_OK",
    points: ["Structured Boolean expressions", "Readable and machine-usable", "Relationships preserved"],
  },
  {
    n: "05",
    title: "Cross-reference",
    body: "Every signal can become part of a larger dependency map, turning individual rungs into a system-level view of the documented logic.",
    points: ["Where is this written?", "Where is it read?", "How many writers?", "What depends on it?"],
  },
  {
    n: "06",
    title: "Analyse",
    body: "The engine identifies conditions that deserve engineering review, and reports unresolved or ambiguous structures rather than silently inventing an interpretation.",
    points: ["Dangling reads", "Write-only signals", "Multiple writers", "Orphaned logic", "Latch / state behaviour", "Unresolved elements"],
  },
  {
    n: "07",
    title: "Export",
    body: "The same analysis produces several engineering outputs. One source, one analysis, multiple useful outputs.",
    points: ["Boolean equations · JSON", "Rung and network data", "Markdown · HTML reports", "CSV · XLSX workbooks", "Supported logic handoff"],
  },
] as const;

export function PipelineSection() {
  return (
    <Section id="pipeline" index="02" label="Analysis pipeline" tone="paper">
      <div className="pb-16 pt-10 sm:pb-24 sm:pt-14">
        <div className="max-w-2xl">
          <SectionTitle id="pipeline-title">From ladder diagrams to engineering intelligence.</SectionTitle>
          <Lede className="mt-6">
            The engine follows a structured, deterministic analysis pipeline. Every stage is explicit about what it can
            reconstruct and what it cannot.
          </Lede>
        </div>

        <div className="mt-12">
          <MicroLabel>Pipeline</MicroLabel>
          <div className="mt-3">
            <FlowStrip />
          </div>
        </div>

        <ol className="mt-14 grid gap-px border border-hair bg-hair sm:grid-cols-2 lg:grid-cols-3">
          {PIPELINE.map((stage, i) => (
            <Reveal as="li" key={stage.n} delay={i * 70} className="flex flex-col bg-paper p-6 sm:p-7">
              <div className="flex items-baseline gap-3">
                <span className="font-mono text-[11px] tabular-nums text-brand-800">{stage.n}</span>
                <h3 className="text-lg font-semibold text-ink">{stage.title}</h3>
              </div>
              <p className="mt-3 text-sm leading-relaxed text-ink-2">{stage.body}</p>

              {"equation" in stage ? (
                <p className="mt-4 border border-hair bg-paper-2 px-3 py-2.5 font-mono text-[11.5px] leading-relaxed text-ink">
                  {stage.equation}
                </p>
              ) : null}

              <ul className="mt-5 flex flex-wrap gap-1.5 border-t border-hair pt-4">
                {stage.points.map((point) => (
                  <li
                    key={point}
                    className="border border-hair bg-paper-2 px-2 py-1 font-mono text-[10px] uppercase tracking-[0.1em] text-ink-3"
                  >
                    {point}
                  </li>
                ))}
              </ul>
            </Reveal>
          ))}
        </ol>

        <p className="mt-6 text-xs leading-relaxed text-ink-3">
          Analysis findings are results for engineering review — not an automatic replacement for qualified engineering
          judgment. Dialect support and output formats vary by source.
        </p>

        <Reveal className="mt-12">
          <EngineConsole />
        </Reveal>
      </div>
    </Section>
  );
}

/* ================================================================== *
 * 03 — Structure, not words
 * ================================================================== */

const NOT_THE_SAME = [
  {
    a: "A contact in series",
    b: "A contact in parallel",
    note: "Different logic, different meaning, different equation.",
  },
  {
    a: "A branch",
    b: "Another line of text",
    note: "A branch changes the path a signal can take.",
  },
  {
    a: "A coil",
    b: "Another symbol",
    note: "A coil is usually where a signal is written.",
  },
] as const;

const FROM_SIDE = ["Contacts", "Branches", "Coils", "Timers", "Counters", "Signals", "Relationships"];
const TO_SIDE = ["Logic model", "Boolean equations", "Signal cross-references", "Dependency analysis", "Engineering reports", "Machine-readable data"];

function Column({ label, items, tone }: { label: string; items: readonly string[]; tone: "from" | "to" }) {
  return (
    <div className="flex-1">
      <MicroLabel tone={tone === "to" ? "ink" : "paper"}>{label}</MicroLabel>
      <ul className="mt-4 space-y-2">
        {items.map((item) => (
          <li
            key={item}
            className={`border px-3 py-2 text-[12.5px] leading-snug ${
              tone === "to" ? "border-white/15 bg-white/[0.04] text-mist-200" : "border-hair bg-paper-2 text-ink-2"
            }`}
          >
            {item}
          </li>
        ))}
      </ul>
    </div>
  );
}

export function StructureSection() {
  return (
    <Section id="structure" index="03" label="Structure, not words" tone="tint">
      <div className="pb-16 pt-10 sm:pb-24 sm:pt-14">
        <div className="grid gap-8 lg:grid-cols-[1fr_1.15fr] lg:gap-16">
          <div>
            <SectionTitle id="structure-title">It does not just read the words.</SectionTitle>
            <Lede className="mt-6">
              A ladder document contains meaning in its structure. The engine works with the relationships between
              elements rather than treating the source as ordinary text.
            </Lede>
            <p className="mt-6 text-balance text-lg font-semibold leading-snug text-ink">
              Document structure becomes logic structure — and that is what makes the data useful for analysis.
            </p>
          </div>

          <ul className="space-y-px">
            {NOT_THE_SAME.map((row, i) => (
              <Reveal as="li" key={row.a} delay={i * 90} className="border-t border-hair py-5 first:border-t-0 first:pt-0">
                <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1 text-sm">
                  <span className="font-semibold text-ink">{row.a}</span>
                  <span aria-hidden="true" className="text-ink-4">
                    is not the same as
                  </span>
                  <span className="font-semibold text-ink">{row.b}</span>
                </div>
                <p className="mt-2 text-[13px] leading-relaxed text-ink-2">{row.note}</p>
              </Reveal>
            ))}
          </ul>
        </div>

        {/* transformation */}
        <Reveal className="mt-14 grid gap-px border border-hair bg-hair sm:grid-cols-[1fr_auto_1fr]">
          <div className="bg-paper p-6 sm:p-8">
            <Column label="From this" items={FROM_SIDE} tone="from" />
          </div>

          <div aria-hidden="true" className="flex items-center justify-center bg-paper px-4 py-2 sm:px-6">
            <span className="flex flex-col items-center gap-2">
              <span className="march-line-y h-10 w-px" />
              <span className="font-mono text-[10px] uppercase tracking-[0.2em] text-brand-800">becomes</span>
              <span className="march-line-y h-10 w-px" />
            </span>
          </div>

          <div className="bg-ink p-6 sm:p-8">
            <Column label="To this" items={TO_SIDE} tone="to" />
          </div>
        </Reveal>

        <p className="mt-6 text-balance text-base font-semibold text-ink">
          Make industrial logic understandable to both engineers and software.
        </p>
      </div>
    </Section>
  );
}

/* ================================================================== *
 * 04 — Cross-reference intelligence
 * ================================================================== */

const XREF_QUESTIONS = [
  "Where is this signal written?",
  "Where is it read?",
  "How many writers does it have?",
  "What logic depends on it?",
  "Is it referenced without a detected writer?",
  "Is it written but apparently unused?",
] as const;

export function CrossReferenceSection() {
  return (
    <Section id="cross-reference" index="04" label="Cross-reference" tone="paper">
      <div className="pb-16 pt-10 sm:pb-24 sm:pt-14">
        <div className="grid gap-10 lg:grid-cols-[1fr_1.1fr] lg:gap-16">
          <div>
            <SectionTitle id="cross-reference-title">Follow a signal through the system.</SectionTitle>
            <Lede className="mt-6">
              A signal may appear in many places across a large control system. Finding those relationships manually is
              time-consuming; XFORCEMSW creates a structured view of signal usage.
            </Lede>

            <ul className="mt-9 space-y-px">
              {XREF_QUESTIONS.map((q, i) => (
                <Reveal as="li" key={q} delay={i * 60} className="flex items-start gap-3 border-t border-hair py-3.5 first:border-t-0">
                  <span aria-hidden="true" className="mt-[7px] h-1.5 w-1.5 shrink-0 bg-brand-800" />
                  <span className="text-sm font-medium text-ink">{q}</span>
                </Reveal>
              ))}
            </ul>
          </div>

          {/* worked example */}
          <Reveal delay={100} className="border border-hair bg-paper">
            <div className="flex items-baseline justify-between gap-4 border-b border-hair px-6 py-4">
              <MicroLabel>Example</MicroLabel>
              <span className="font-mono text-[11px] text-ink-3">Signal</span>
            </div>

            <div className="px-6 py-5">
              <p className="font-mono text-lg font-semibold tracking-[0.04em] text-ink">MOTOR_RUN</p>
            </div>

            <div className="grid gap-px border-y border-hair bg-hair sm:grid-cols-2">
              <div className="bg-paper px-6 py-5">
                <MicroLabel>Written by</MicroLabel>
                <p className="mt-3 font-mono text-[13px] text-ink-2">Page 12 · Rung 42</p>
              </div>
              <div className="bg-paper px-6 py-5">
                <MicroLabel>Read by</MicroLabel>
                <ul className="mt-3 space-y-1 font-mono text-[13px] text-ink-2">
                  <li>Page 14 · Rung 57</li>
                  <li>Page 18 · Rung 81</li>
                  <li>Page 23 · Rung 96</li>
                </ul>
              </div>
            </div>

            <div className="grid grid-cols-2 gap-px bg-hair">
              <div className="bg-ink px-6 py-5">
                <MicroLabel tone="ink">Writers</MicroLabel>
                <p className="mt-2 text-2xl font-semibold tabular-nums text-white">1</p>
              </div>
              <div className="bg-ink px-6 py-5">
                <MicroLabel tone="ink">Detected readers</MicroLabel>
                <p className="mt-2 text-2xl font-semibold tabular-nums text-white">3</p>
              </div>
            </div>

            <p className="px-6 py-5 text-sm leading-relaxed text-ink-2">
              This makes it easier to understand how logic propagates through the documented system.
            </p>
          </Reveal>
        </div>
      </div>
    </Section>
  );
}

/* ================================================================== *
 * 05 — Engineering review
 * ================================================================== */

const USE_CASES = [
  { title: "Logic review", body: "Understand the reconstructed logic and its relationships." },
  { title: "QA", body: "Identify conditions that require engineering attention." },
  { title: "Documentation", body: "Generate consistent reports from the same source." },
  { title: "Troubleshooting", body: "Trace signals through documented logic." },
  { title: "Modernization", body: "Extract information from legacy documentation." },
  { title: "Comparison", body: "Compare a structured representation against another revision or reference." },
  { title: "Knowledge preservation", body: "Make legacy control logic easier to search and understand." },
  { title: "Validation", body: "Work from structured results instead of hundreds of pages." },
] as const;

export function ReviewSection() {
  return (
    <Section id="review" index="05" label="Engineering review" tone="ink">
      <div className="pb-16 pt-10 sm:pb-24 sm:pt-14">
        <div className="grid gap-8 lg:grid-cols-[1fr_1.1fr] lg:gap-16">
          <div>
            <SectionTitle id="review-title" tone="ink">
              Turn a document into something you can inspect.
            </SectionTitle>
            <Lede tone="ink" className="mt-6">
              Instead of opening hundreds of pages and manually searching for every reference, engineers can work from
              structured results.
            </Lede>
          </div>

          <ul className="grid gap-px border border-white/12 bg-white/12 sm:grid-cols-2">
            {USE_CASES.map((item, i) => (
              <Reveal as="li" key={item.title} delay={i * 55} className="bg-ink p-6">
                <h3 className="text-[15px] font-semibold text-white">{item.title}</h3>
                <p className="mt-2 text-[13px] leading-relaxed text-mist-300">{item.body}</p>
              </Reveal>
            ))}
          </ul>
        </div>
      </div>
    </Section>
  );
}

/* ================================================================== *
 * 06 — Architecture
 * ================================================================== */

const ARCHITECTURE = [
  { box: "Industrial sources", detail: "PDF / ASCII / supported exports" },
  { box: "Input processing", detail: "Text / geometry / document structure" },
  { box: "Dialect parser", detail: "Vendor-specific interpretation" },
  { box: "Logic model", detail: "Rungs / networks / elements / signals" },
  { box: "Analysis engine", detail: "Boolean / xref / dependencies / QA" },
  { box: "Engineering outputs", detail: "JSON / CSV / XLSX / HTML / logic outputs" },
] as const;

export function ArchitectureSection() {
  return (
    <Section id="architecture" index="06" label="Architecture" tone="tint">
      <div className="pb-16 pt-10 sm:pb-24 sm:pt-14">
        <div className="max-w-2xl">
          <SectionTitle id="architecture-title">Source → Parser → Logic Model → Analysis → Outputs.</SectionTitle>
          <Lede className="mt-6">
            Each stage has one job. The logic model is the durable artefact in the middle: everything before it reads
            the document, everything after it is derived from the model.
          </Lede>
        </div>

        <ol className="mt-12 grid gap-px border border-hair bg-hair md:grid-cols-2 lg:grid-cols-3">
          {ARCHITECTURE.map((stage, i) => (
            <Reveal as="li" key={stage.box} delay={i * 70} className="relative bg-paper p-6 sm:p-7">
              <span className="font-mono text-[10px] uppercase tracking-[0.2em] text-ink-4">
                {String(i + 1).padStart(2, "0")}
              </span>
              <h3 className="mt-3 text-base font-semibold text-ink">{stage.box}</h3>
              <p className="mt-2 font-mono text-[11px] leading-relaxed text-ink-3">{stage.detail}</p>
              {i < ARCHITECTURE.length - 1 ? (
                <span
                  aria-hidden="true"
                  className="absolute -right-px top-1/2 hidden h-px w-3 -translate-y-1/2 bg-hair-2 lg:block"
                />
              ) : null}
            </Reveal>
          ))}
        </ol>
      </div>
    </Section>
  );
}

/* ================================================================== *
 * 07 — Comparison
 * ================================================================== */

const COMPARE = [
  { manual: "Open the document", xforce: "Load the source" },
  { manual: "Inspect each rung", xforce: "Parse supported logic" },
  { manual: "Manually reconstruct equations", xforce: "Generate Boolean representations" },
  { manual: "Search signal references", xforce: "Build cross-references" },
  { manual: "Follow dependencies manually", xforce: "Analyze signal relationships" },
  { manual: "Create reports manually", xforce: "Generate structured reports" },
  { manual: "Repeat for every revision", xforce: "Re-run the same analysis pipeline" },
  { manual: "Store information mainly in documents", xforce: "Produce machine-readable engineering data" },
] as const;

export function ComparisonSection() {
  return (
    <Section id="compare" index="07" label="Comparison" tone="paper">
      <div className="pb-16 pt-10 sm:pb-24 sm:pt-14">
        <div className="grid gap-8 lg:grid-cols-[1fr_1.2fr] lg:gap-16">
          <div>
            <SectionTitle id="compare-title">Stop manually extracting the same logic.</SectionTitle>
            <Lede className="mt-6">
              The goal is not simply to make document reading faster. It is to make the information inside the document
              reusable.
            </Lede>
          </div>

          <Reveal className="overflow-x-auto">
            <table className="w-full min-w-[34rem] border-collapse text-left">
              <caption className="sr-only">
                Comparison of a traditional manual engineering workflow and the XFORCEMSW analysis pipeline
              </caption>
              <thead>
                <tr className="border-b border-ink">
                  <th scope="col" className="w-[40%] py-4 pr-4 align-bottom">
                    <span className="font-mono text-[10px] uppercase tracking-[0.18em] text-ink-3">
                      Traditional workflow
                    </span>
                  </th>
                  <th scope="col" className="w-[8%] py-4 align-bottom" />
                  <th scope="col" className="w-[52%] py-4 pl-4 align-bottom">
                    <span className="border-b-2 border-brand-800 pb-1.5 font-mono text-[10px] uppercase tracking-[0.18em] text-brand-800">
                      XFORCEMSW
                    </span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {COMPARE.map((row) => (
                  <tr key={row.manual} className="border-b border-hair align-top">
                    <th scope="row" className="py-4 pr-4 text-sm font-normal leading-relaxed text-ink-3">
                      {row.manual}
                    </th>
                    <td className="py-4 text-ink-4" aria-hidden="true">
                      &rarr;
                    </td>
                    <td className="py-4 pl-4 text-sm font-semibold leading-relaxed text-ink">{row.xforce}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Reveal>
        </div>
      </div>
    </Section>
  );
}

/* ================================================================== *
 * 08 — Deterministic and offline
 * ================================================================== */

const STACK = [
  { name: "C# / .NET 7", role: "Application and analysis engine" },
  { name: "WPF", role: "Windows engineering interface" },
  { name: "CLI", role: "Automation and batch workflows" },
  { name: "PdfPig", role: "PDF text and geometry extraction" },
  { name: "Custom parsers", role: "PLC dialect interpretation" },
  { name: "Domain model", role: "Rungs, signals, Boolean expressions" },
  { name: "xUnit", role: "Automated testing" },
  { name: "Export engines", role: "JSON · CSV · XLSX · HTML · MD" },
] as const;

const DETERMINISM = [
  "Repeatability",
  "Traceability",
  "Testability",
  "Offline operation",
  "Predictable processing",
  "Transparent limitations",
] as const;

export function DeterministicSection() {
  return (
    <Section id="deterministic" index="08" label="Deterministic · offline" tone="tint">
      <div className="pb-16 pt-10 sm:pb-24 sm:pt-14">
        <div className="grid gap-10 lg:grid-cols-[1.1fr_1fr] lg:gap-16">
          <div>
            <SectionTitle id="deterministic-title">Your engineering documents stay in the engineering environment.</SectionTitle>
            <Lede className="mt-6">
              The core tool is a Windows C#/.NET 7 application and does not require an external AI service or cloud API to
              perform its deterministic logic analysis. Analysis should not depend on who happened to read the document.
            </Lede>
            <p className="mt-6 text-sm leading-relaxed text-ink-3">
              The same supported source can be processed through the same pipeline and produce repeatable structured
              results — which enables repeatable documentation, automated processing, regression testing, revision
              comparison and consistent cross-reference generation.
            </p>
            <div className="mt-8 flex flex-wrap gap-3">
              <ActionLink href="/contact" size="md">
                Contact us
              </ActionLink>
            </div>
          </div>

          <ul className="grid grid-cols-2 gap-px self-start border border-hair bg-hair sm:grid-cols-3">
            {STACK.map((item, i) => (
              <Reveal as="li" key={item.name} delay={i * 50} className="ticks group bg-paper p-5 transition-colors duration-300 hover:bg-paper-2">
                <h3 className="text-sm font-semibold text-ink">{item.name}</h3>
                <p className="mt-1.5 font-mono text-[10px] uppercase leading-relaxed tracking-[0.1em] text-ink-3">
                  {item.role}
                </p>
                <span
                  aria-hidden="true"
                  className="mt-4 block h-px w-0 bg-brand-800 transition-all duration-500 group-hover:w-full"
                />
              </Reveal>
            ))}
          </ul>
        </div>

        <Reveal className="mt-14 grid gap-px border border-hair bg-hair lg:grid-cols-[1fr_1.25fr]">
          <div className="bg-ink p-7 sm:p-9">
            <MicroLabel tone="ink">Principle</MicroLabel>
            <h3 className="mt-5 text-balance text-2xl font-semibold leading-snug text-white">
              Deterministic first. Intelligence where it belongs.
            </h3>
            <p className="mt-4 text-sm leading-relaxed text-mist-300">
              Industrial logic should not be guessed. The core analysis is based on explicit parsing and engineering
              rules.
            </p>
          </div>
          <div className="bg-paper p-7 sm:p-9">
            <MicroLabel>What that gives you</MicroLabel>
            <ul className="mt-5 flex flex-wrap gap-2">
              {DETERMINISM.map((item) => (
                <li
                  key={item}
                  className="border border-hair bg-paper-2 px-3 py-1.5 font-mono text-[11px] uppercase tracking-[0.12em] text-ink-2"
                >
                  {item}
                </li>
              ))}
            </ul>
            <p className="mt-6 text-sm leading-relaxed text-ink-2">
              If AI assistance is added later, it can sit above the deterministic analysis layer — helping engineers
              explain or explore results without silently replacing the underlying logic reconstruction.
            </p>
          </div>
        </Reveal>
      </div>
    </Section>
  );
}

/* ================================================================== *
 * 09 — Artefacts from one source
 * ================================================================== */

const ARTEFACTS = [
  { title: "Boolean equations", body: "Readable and machine-usable representations of reconstructed logic." },
  { title: "Cross-reference data", body: "Signal readers, writers and relationships." },
  { title: "Rung and network index", body: "Structured navigation through the analyzed logic." },
  { title: "Engineering reports", body: "Readable summaries and analysis findings." },
  { title: "Excel workbooks", body: "Useful for engineering review and data inspection." },
  { title: "JSON / CSV", body: "Ready for downstream processing and integration." },
  { title: "HTML", body: "A browsable representation of the analysis." },
  { title: "Supported logic outputs", body: "For workflows such as TRISTATION / MSW where applicable." },
] as const;

export function ArtefactsSection() {
  return (
    <Section id="artefacts" index="09" label="Artefacts" tone="paper">
      <div className="pb-16 pt-10 sm:pb-24 sm:pt-14">
        <div className="grid gap-8 lg:grid-cols-[1fr_1.2fr] lg:gap-16">
          <div>
            <SectionTitle id="artefacts-title">One document. Multiple engineering artefacts.</SectionTitle>
            <Lede className="mt-6">
              The source does not need to be interpreted repeatedly for every output. The analysis becomes the reusable
              engineering layer.
            </Lede>
            <p className="mt-6 text-sm leading-relaxed text-ink-3">
              TRISTATION / MSW handoff is one of the supported engineering outputs — not the identity of the product.
            </p>
          </div>

          <ul className="grid gap-px border border-hair bg-hair sm:grid-cols-2">
            {ARTEFACTS.map((item, i) => (
              <Reveal as="li" key={item.title} delay={i * 55} className="bg-paper p-6">
                <h3 className="text-[15px] font-semibold leading-snug text-ink">{item.title}</h3>
                <p className="mt-2 text-[13px] leading-relaxed text-ink-2">{item.body}</p>
              </Reveal>
            ))}
          </ul>
        </div>
      </div>
    </Section>
  );
}

/* ================================================================== *
 * 10 — Boundaries
 * ================================================================== */

const NOT_THIS = [
  "A PLC runtime",
  "A PLC programming IDE",
  "A safety certification system",
  "A replacement for qualified engineering review",
  "A universal parser for every PLC language",
  "A live plant-control system",
] as const;

export function BoundariesSection() {
  return (
    <Section id="boundaries" index="10" label="Boundaries" tone="tint">
      <div className="pb-16 pt-10 sm:pb-24 sm:pt-14">
        <div className="grid gap-8 lg:grid-cols-[1fr_1.1fr] lg:gap-16">
          <div>
            <SectionTitle id="boundaries-title">Built to analyze. Not to pretend.</SectionTitle>
            <Lede className="mt-6">
              XFORCEMSW is an engineering analysis and documentation tool. It is not, by itself:
            </Lede>
          </div>

          <ul className="grid gap-px border border-hair bg-hair sm:grid-cols-2">
            {NOT_THIS.map((item) => (
              <li key={item} className="flex items-start gap-3 bg-paper p-5">
                <span aria-hidden="true" className="mt-[3px] shrink-0 font-mono text-[13px] leading-none text-bad-500">
                  &times;
                </span>
                <span className="text-[13.5px] leading-relaxed text-ink-2">{item}</span>
              </li>
            ))}
          </ul>
        </div>

        <Reveal className="mt-12 grid gap-8 border-t border-hair pt-10 lg:grid-cols-2">
          <p className="text-sm leading-relaxed text-ink-2">
            The engine reports what it can reconstruct from the supplied source and identifies unresolved or ambiguous
            information rather than silently inventing an interpretation.
          </p>
          <p className="text-balance text-base font-semibold leading-snug text-ink">
            That distinction matters in industrial software.
          </p>
        </Reveal>
      </div>
    </Section>
  );
}

/* ================================================================== *
 * 11 — Modernization foundation
 * ================================================================== */

const MODERN_FLOW = [
  { step: "Legacy documentation", items: ["PDF", "ASCII listing", "Vendor export"] },
  { step: "XFORCEMSW", items: ["Parse", "Reconstruct", "Analyze", "Cross-reference"] },
  { step: "Structured engineering data", items: ["Boolean logic", "Signals", "Dependencies", "Rung maps", "Reports"] },
  { step: "Future workflows", items: ["Migration", "Comparison", "Documentation", "QA", "Custom tooling"] },
] as const;

export function ModernizationSection() {
  return (
    <Section id="modernization" index="11" label="Modernization" tone="paper">
      <div className="pb-16 pt-10 sm:pb-24 sm:pt-14">
        <div className="max-w-2xl">
          <SectionTitle id="modernization-title">
            Don&rsquo;t throw away the engineering knowledge inside legacy documents.
          </SectionTitle>
          <Lede className="mt-6">
            A modernization project often begins with understanding what the existing system actually does. Structured
            extraction provides the intermediate layer between legacy documentation and future engineering systems.
          </Lede>
        </div>

        <ol className="mt-12 grid gap-px border border-hair bg-hair sm:grid-cols-2 lg:grid-cols-4">
          {MODERN_FLOW.map((stage, i) => (
            <Reveal as="li" key={stage.step} delay={i * 80} className="flex flex-col bg-paper p-6">
              <div className="flex items-baseline gap-3">
                <span className="font-mono text-[11px] tabular-nums text-brand-800">
                  {String(i + 1).padStart(2, "0")}
                </span>
                <h3 className="text-sm font-semibold leading-snug text-ink">{stage.step}</h3>
              </div>
              <ul className="mt-4 flex flex-wrap gap-1.5 border-t border-hair pt-4">
                {stage.items.map((item) => (
                  <li
                    key={item}
                    className="border border-hair bg-paper-2 px-2 py-1 font-mono text-[10px] uppercase tracking-[0.1em] text-ink-3"
                  >
                    {item}
                  </li>
                ))}
              </ul>
              {i < MODERN_FLOW.length - 1 ? (
                <span aria-hidden="true" className="march-line-x mt-5 h-px w-full lg:hidden" />
              ) : null}
            </Reveal>
          ))}
        </ol>

        <div className="mt-14 grid gap-8 lg:grid-cols-2">
          <Reveal className="border border-hair bg-paper p-7">
            <MicroLabel>Before</MicroLabel>
            <p className="mt-4 text-sm font-semibold text-ink">Human-readable</p>
            <ul className="mt-3 space-y-1 font-mono text-[12.5px] text-ink-3">
              <li>PDF</li>
              <li>Drawing</li>
              <li>Listing</li>
              <li>Printout</li>
            </ul>
          </Reveal>

          <Reveal delay={100} className="border border-hair bg-ink p-7">
            <MicroLabel tone="ink">After</MicroLabel>
            <p className="mt-4 text-sm font-semibold text-white">Human-readable + machine-readable</p>
            <ul className="mt-3 space-y-1 font-mono text-[12.5px] text-mist-300">
              <li>Logic model</li>
              <li>Boolean equations</li>
              <li>Cross-references</li>
              <li>Dependencies</li>
              <li>Reports and structured datasets</li>
            </ul>
          </Reveal>
        </div>
      </div>
    </Section>
  );
}

/* ================================================================== *
 * 12 — Access
 * ================================================================== */

const PATHS = [
  {
    step: "01",
    title: "I already have a license",
    body: "Paste the key and you are in. No account form, no password — the key is the credential and your email only names the seat.",
    cta: { href: "/enter-license", label: "Enter your license" },
    primary: true,
  },
  {
    step: "02",
    title: "I need a license",
    body: "Pick a plan and a license is generated for your account the moment checkout completes. Start free, or pay monthly or yearly.",
    cta: { href: "/pricing", label: "See license pricing" },
    primary: false,
  },
  {
    step: "03",
    title: "I need to talk to someone",
    body: "Enterprise rollout, on-premise engine deployment, a pilot on your own drawings, or a scope you have not priced yet.",
    cta: { href: "/contact", label: "Contact us" },
    primary: false,
  },
] as const;

export function AccessSection() {
  return (
    <Section id="access" index="12" label="Access" tone="tint">
      <div className="pb-16 pt-10 sm:pb-24 sm:pt-14">
        <div className="max-w-2xl">
          <SectionTitle id="access-title">Three ways in. No form you do not need.</SectionTitle>
          <Lede className="mt-6">
            A license is the only thing standing between you and the pipeline. Bring one, buy one, or ask us a
            question — there is no third option and no sales gate on the free plan.
          </Lede>
        </div>

        <ul className="mt-12 grid gap-px border border-hair bg-hair sm:grid-cols-3">
          {PATHS.map((path, i) => (
            <Reveal as="li" key={path.step} delay={i * 90} className="flex flex-col bg-paper p-7 sm:p-8">
              <span className="font-mono text-[11px] tabular-nums text-brand-800">{path.step}</span>
              <h3 className="mt-5 text-lg font-semibold leading-snug text-ink">{path.title}</h3>
              <p className="mt-3 flex-1 text-sm leading-relaxed text-ink-2">{path.body}</p>
              <div className="mt-6">
                <ActionLink href={path.cta.href} tone={path.primary ? "solid" : "outline"} size="md" className="w-full">
                  {path.cta.label}
                </ActionLink>
              </div>
            </Reveal>
          ))}
        </ul>
      </div>
    </Section>
  );
}

/* ================================================================== *
 * 13 — License pricing
 * ================================================================== */

const SELF_SERVE: PlanId[] = PLAN_ORDER.filter((id) => PLANS[id].priceMonthly !== null);

export function PlansSection() {
  return (
    <Section id="plans" index="13" label="License pricing" tone="paper">
      <div className="pb-16 pt-10 sm:pb-24 sm:pt-14">
        <div className="grid gap-8 lg:grid-cols-[1fr_1.15fr] lg:gap-16">
          <div>
            <SectionTitle id="plans-title">License pricing, on the page.</SectionTitle>
            <Lede className="mt-6">
              Every plan runs the full pipeline. The difference is quota, file size, seats, how many keys you can hold
              and how long results are kept.
            </Lede>
            <p className="mt-5 text-sm leading-relaxed text-ink-3">
              Checkout is currently handled by a mock provider in this build — no card is collected, and a license is
              generated immediately so you can try the whole flow.
            </p>
          </div>

          <ul className="grid gap-px border border-hair bg-hair sm:grid-cols-2">
            {SELF_SERVE.map((id, i) => {
              const plan = PLANS[id];
              return (
                <Reveal
                  as="li"
                  key={id}
                  delay={i * 80}
                  className="flex flex-col bg-paper p-6 transition-colors duration-300 hover:bg-paper-2 sm:p-7"
                >
                  <div className="flex items-baseline justify-between gap-3">
                    <h3 className="text-base font-semibold text-ink">{plan.name}</h3>
                    <p className="shrink-0 text-right">
                      <span className="text-xl font-semibold tabular-nums text-ink">
                        {plan.priceMonthly === 0 ? "Free" : `$${plan.priceMonthly}`}
                      </span>
                      {plan.priceMonthly !== 0 ? <span className="text-[11px] text-ink-3">/mo</span> : null}
                    </p>
                  </div>

                  <p className="mt-2 text-[13px] leading-relaxed text-ink-2">{plan.tagline}</p>

                  <dl className="mt-5 space-y-1.5 border-t border-hair pt-4 text-[12px]">
                    {[
                      [
                        "Conversions",
                        Number.isFinite(plan.monthlyConversions) ? `${plan.monthlyConversions} / month` : "Unlimited",
                      ],
                      ["Max file", formatBytes(plan.maxUploadBytes)],
                      ["Seats", String(plan.seats)],
                      ["License keys", String(plan.licenseKeys)],
                    ].map(([label, value]) => (
                      <div key={label} className="flex items-baseline justify-between gap-3">
                        <dt className="text-ink-3">{label}</dt>
                        <dd className="text-right font-medium tabular-nums text-ink">{value}</dd>
                      </div>
                    ))}
                  </dl>

                  <div className="mt-6 flex-1" />
                  <ActionLink
                    href={`/checkout?plan=${id}`}
                    tone={id === "professional" ? "solid" : "outline"}
                    size="md"
                    className="w-full"
                  >
                    {plan.priceMonthly === 0 ? "Start free" : "Get this license"}
                  </ActionLink>
                </Reveal>
              );
            })}

            <Reveal as="li" delay={SELF_SERVE.length * 80} className="flex flex-col bg-paper p-6 sm:p-7">
              <div className="flex items-baseline justify-between gap-3">
                <h3 className="text-base font-semibold text-ink">Enterprise</h3>
                <p className="shrink-0 text-[11px] uppercase tracking-[0.14em] text-ink-3">Custom</p>
              </div>
              <p className="mt-2 flex-1 text-[13px] leading-relaxed text-ink-2">
                On-premise engine deployment, SSO, audit log export, volume quotes and SLAs.
              </p>
              <div className="mt-6">
                <ActionLink href="/contact?source=enterprise" tone="outline" size="md" className="w-full">
                  Contact us
                </ActionLink>
              </div>
            </Reveal>
          </ul>
        </div>
      </div>
    </Section>
  );
}

/* ================================================================== *
 * 14 — About
 * ================================================================== */

const PRINCIPLES = [
  {
    title: "Deterministic, not guessed",
    body: "Ladder logic has precise semantics. The software reconstructs it with explicit parsing and engineering rules rather than approximating it.",
  },
  {
    title: "Repeatable output",
    body: "The same source should produce the same structure every time, so a drawing can change without the process drifting.",
  },
  {
    title: "Open the handoff",
    body: "Structured data should be usable by the next tool, not locked inside the tool that produced it.",
  },
  {
    title: "State the limits",
    body: "Unresolved and ambiguous logic is reported rather than invented. That distinction matters in industrial software.",
  },
] as const;

export function AboutSection() {
  return (
    <Section id="about" index="14" label="About" tone="tint">
      <div className="pb-16 pt-10 sm:pb-24 sm:pt-14">
        <div className="grid gap-8 lg:grid-cols-[1.15fr_1fr] lg:gap-16">
          <div>
            <SectionTitle id="about-title">
              XFORCEMSW builds engineering software for people who work with ladder logic.
            </SectionTitle>
            <Lede className="mt-6 max-w-xl">
              From drawings to engineering data. The product is not simply a converter — it is a way to move industrial
              control logic from a document representation into a structured representation, so tools and workflows that
              are difficult to build become possible.
            </Lede>
            <div className="mt-8 flex flex-wrap gap-3">
              <ActionLink href="/pricing" size="md">
                Get a license
              </ActionLink>
              <ActionLink href="/contact" tone="outline" size="md">
                Contact us
              </ActionLink>
            </div>
          </div>

          <ul className="space-y-px">
            {PRINCIPLES.map((item, i) => (
              <Reveal as="li" key={item.title} delay={i * 90} className="border-t border-hair py-6 first:border-t-0 first:pt-0 sm:py-7">
                <h3 className="text-base font-semibold text-ink">{item.title}</h3>
                <p className="mt-2 text-sm leading-relaxed text-ink-2">{item.body}</p>
              </Reveal>
            ))}
          </ul>
        </div>
      </div>
    </Section>
  );
}
