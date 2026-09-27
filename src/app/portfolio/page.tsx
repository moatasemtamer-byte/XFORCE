import type { Metadata } from "next";
import { MarketingShell, PageHeader } from "@/components/marketing-shell";
import { Badge, ButtonLink, Panel, SectionHeading } from "@/components/ui";

export const metadata: Metadata = {
  title: "Portfolio",
  description: "Selected ladder diagram to MSW conversion work delivered by XforceMsw.",
};

const PROJECTS = [
  {
    client: "Beverage bottling line",
    sector: "Packaging",
    scope: "Motor starter and conveyor interlock set",
    volume: "312 source drawings",
    turnaround: "4 working days",
    outcome:
      "Every rung traced back to its source sheet. Two legacy drawings with mismatched coil references were flagged by the engine before hand-off.",
    tags: ["Motor starters", "Conveyor interlocks", "Traceability"],
  },
  {
    client: "Municipal pump station",
    sector: "Water infrastructure",
    scope: "Panel IO list and rung report generation",
    volume: "18 drawings",
    turnaround: "6 minutes",
    outcome:
      "IO list and rung report produced per panel, then reviewed by the site's own controls engineer against the as-built documentation.",
    tags: ["IO lists", "Rung reports", "Field validation"],
  },
  {
    client: "Automotive tier-1 supplier",
    sector: "Automotive",
    scope: "Repeatable monthly service for a cell",
    volume: "640 drawings / quarter",
    turnaround: "Continuous",
    outcome:
      "Moved from a two-week manual pass to a queued batch with per-job audit records, tracked on the Professional plan with five license keys.",
    tags: ["Batch processing", "Recurring service", "Licensing"],
  },
  {
    client: "Machine builder (OEM)",
    sector: "OEM",
    scope: "Client-facing deliverable bundles per machine",
    volume: "1,100 bundles",
    turnaround: "Continuous",
    outcome:
      "Each machine ships with its MSW bundle and a signed job record, so the customer's maintenance team receives the same documentation the build team used.",
    tags: ["Client handover", "Artifact bundles", "Job records"],
  },
];

const CAPABILITIES = [
  { label: "Largest single job", value: "1,240 rungs" },
  { label: "Fastest turnaround", value: "6 minutes" },
  { label: "Drawings converted", value: "9,400+" },
  { label: "Repeat clients", value: "4" },
];

export default function PortfolioPage() {
  return (
    <MarketingShell>
      <PageHeader
        eyebrow="Portfolio"
        title="Work we have actually delivered"
        description="A few engagements that show the shape of the work: what came in, how long it took, and what the client did with it."
      />

      <section className="border-b border-ink-800">
        <div className="mx-auto max-w-6xl px-5 py-14">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {CAPABILITIES.map((item) => (
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

      <section className="mx-auto max-w-6xl px-5 py-16">
        <div className="space-y-6">
          {PROJECTS.map((project) => (
            <Panel key={project.client} className="p-6 sm:p-8">
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                  <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-signal-400">
                    {project.sector}
                  </p>
                  <h2 className="mt-2 text-lg font-semibold">{project.client}</h2>
                  <p className="mt-1 text-sm text-mist-400">{project.scope}</p>
                </div>
                <div className="flex flex-wrap gap-2">
                  {project.tags.map((tag) => (
                    <Badge key={tag}>{tag}</Badge>
                  ))}
                </div>
              </div>

              <dl className="mt-6 grid gap-4 border-y border-ink-800 py-4 sm:grid-cols-2">
                <div>
                  <dt className="text-[11px] uppercase tracking-[0.14em] text-mist-500">Volume</dt>
                  <dd className="mt-1 text-sm tabular-nums text-mist-200">{project.volume}</dd>
                </div>
                <div>
                  <dt className="text-[11px] uppercase tracking-[0.14em] text-mist-500">Turnaround</dt>
                  <dd className="mt-1 text-sm tabular-nums text-mist-200">{project.turnaround}</dd>
                </div>
              </dl>

              <p className="mt-4 text-sm leading-relaxed text-mist-400">{project.outcome}</p>
            </Panel>
          ))}
        </div>
      </section>

      <section className="border-t border-ink-800 bg-ink-900/30">
        <div className="mx-auto max-w-3xl px-5 py-16">
          <SectionHeading
            title="Want a reference call?"
            description="We will put you in touch with a client whose drawings look like yours, once you tell us what you are converting."
          />
          <div className="mt-8 flex flex-wrap justify-center gap-3">
            <ButtonLink href="/contact" size="lg">
              Request a reference
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
