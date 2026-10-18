import type { Metadata } from "next";
import { MarketingShell, PageHeader } from "@/components/marketing-shell";
import { Badge, ButtonLink, Panel, SectionHeading } from "@/components/ui";

export const metadata: Metadata = {
  title: "Engine capabilities",
  description: "Exactly what the XforceMsw conversion engine reads, what it returns, and where its limits are.",
};

const INPUTS = [
  {
    kind: "Ladder-logic PDF",
    accepted: true,
    detail: "Text-based PDF exports of ladder sheets. Pages are read in order and rungs are reconstructed from the printed logic.",
  },
  {
    kind: "Raw program listing",
    accepted: true,
    detail: "Monospace printouts such as a Triconex TRISTATION .LPT file, saved with a .txt, .lst, .log or .csv extension.",
  },
  {
    kind: "Scanned PDF",
    accepted: false,
    detail: "Image-only pages contain no text layer to read. Run OCR on the scan first, then upload the result.",
  },
  {
    kind: "CAD, image and database exports",
    accepted: false,
    detail: "SCH, LAD, XML, EDB, DWG, DXF, PNG and JPG are not parsed. Export to PDF or a text listing instead.",
  },
];

const ARTIFACTS = [
  {
    name: ".tristation.tsv",
    detail: "TRISTATION functional listing in Networks # / Logic form. Produced for Triconex sources; rung numbers are preserved so the output can be diffed against the original.",
    primary: true,
  },
  {
    name: ".report.md",
    detail: "Markdown summary of detected networks, variables and rungs, together with any warnings raised during parsing.",
  },
  {
    name: ".equations.json",
    detail: "Machine-readable equation set, useful for diffing two versions of the same program.",
  },
  {
    name: ".rungs.csv",
    detail: "One row per rung, for review in a spreadsheet or as input to your own checking script.",
  },
  {
    name: ".workbook.xlsx",
    detail: "Formatted workbook holding the equations and rung data for offline review.",
  },
  {
    name: ".report.html",
    detail: "Printable single-file report for attaching to a QA pack or an email.",
  },
];

const LIMITS = [
  { label: "Maximum upload", value: "32 MB" },
  { label: "Maximum pages", value: "400" },
  { label: "Engine timeout", value: "100 s" },
  { label: "Artifacts per job", value: "6" },
];

const BEHAVIOUR = [
  "Triconex sources get the full TRISTATION functional listing. Other recognised dialects are still parsed, but return the report and equation outputs only, with a warning explaining why no functional listing was produced.",
  "The engine reads the logic, it does not interpret it. Every output should be reviewed against the source drawing before it is used on a live machine.",
  "If the source dialect cannot be identified, the engine says so in the warnings rather than returning a plausible-looking but empty conversion.",
];

export default function PortfolioPage() {
  return (
    <MarketingShell>
      <PageHeader
        eyebrow="Engine capabilities"
        title="What the engine actually reads and returns"
        description="No invented case studies here — this is the actual input and output contract of the conversion service."
      />

      <section className="border-b border-ink-800">
        <div className="mx-auto max-w-6xl px-5 py-14">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {LIMITS.map((item) => (
              <Panel key={item.label} className="p-5">
                <p className="text-[11px] font-medium uppercase tracking-[0.14em] text-mist-500">
                  {item.label}
                </p>
                <p className="mt-2 text-2xl font-semibold tabular-nums text-mist-100">{item.value}</p>
              </Panel>
            ))}
          </div>
        </div>
      </section>

      <section className="border-b border-ink-800">
        <div className="mx-auto max-w-6xl px-5 py-16">
          <SectionHeading
            title="Accepted sources"
            description="Two formats are parsed. Everything else is rejected at upload rather than returning an empty result."
          />
          <div className="mt-10 space-y-4">
            {INPUTS.map((item) => (
              <Panel key={item.kind} className="p-6">
                <div className="flex flex-wrap items-center gap-3">
                  <h2 className="text-base font-semibold">{item.kind}</h2>
                  <Badge tone={item.accepted ? "good" : "warn"}>
                    {item.accepted ? "Accepted" : "Not accepted"}
                  </Badge>
                </div>
                <p className="mt-3 text-sm leading-relaxed text-mist-400">{item.detail}</p>
              </Panel>
            ))}
          </div>
        </div>
      </section>

      <section className="border-b border-ink-800 bg-ink-900/30">
        <div className="mx-auto max-w-6xl px-5 py-16">
          <SectionHeading
            title="Returned for every job"
            description="The engine always returns the same set, so there is nothing to configure before you upload."
          />
          <div className="mt-10 grid gap-4 sm:grid-cols-2">
            {ARTIFACTS.map((item) => (
              <Panel key={item.name} className="p-6">
                <div className="flex flex-wrap items-center gap-3">
                  <p className="font-mono text-sm text-signal-300">{item.name}</p>
                  {item.primary ? <Badge tone="info">Primary</Badge> : null}
                </div>
                <p className="mt-3 text-sm leading-relaxed text-mist-400">{item.detail}</p>
              </Panel>
            ))}
          </div>
        </div>
      </section>

      <section className="border-b border-ink-800">
        <div className="mx-auto max-w-4xl px-5 py-16">
          <SectionHeading
            title="What the engine will not do"
            description="The boundaries matter more than the feature list when you are trusting output on a real machine."
          />
          <ul className="mt-8 space-y-4">
            {BEHAVIOUR.map((line) => (
              <li key={line} className="flex gap-3 text-sm leading-relaxed text-mist-400">
                <span className="mt-2 h-1 w-1 shrink-0 rounded-full bg-signal-400" />
                {line}
              </li>
            ))}
          </ul>
        </div>
      </section>

      <section className="bg-ink-900/30">
        <div className="mx-auto max-w-3xl px-5 py-16">
          <SectionHeading
            title="Try it on your own source"
            description="The free plan gives you five conversions a month, which is enough to check the engine against a drawing you already trust."
          />
          <div className="mt-8 flex flex-wrap justify-center gap-3">
            <ButtonLink href="/register" size="lg">
              Start free
            </ButtonLink>
            <ButtonLink href="/pricing" variant="outline" size="lg">
              See pricing
            </ButtonLink>
          </div>
        </div>
      </section>
    </MarketingShell>
  );
}
