import type { Metadata } from "next";
import { MarketingShell, PageHeader } from "@/components/marketing-shell";
import { Badge, ButtonLink, Panel, SectionHeading } from "@/components/ui";

export const metadata: Metadata = {
  title: "Features",
  description: "What the XforceMsw platform does: ladder-aware conversion to TRISTATION notation, licensing, job history and quotas.",
};

const GROUPS = [
  {
    title: "Conversion",
    items: [
      "Sources: ladder-logic PDF, or a raw program listing (.txt, .lpt, .lst, .log, .csv).",
      "Rung-aware parsing: contacts, coils, timers, counters and branches are read as logic.",
      "Triconex sources produce a TRISTATION functional listing (.tristation.tsv).",
      "Rung numbering preserved from source to deliverable for traceability.",
      "Every job also returns a report, equation JSON, rung CSV, workbook and HTML report.",
      "Engine warnings surfaced on the job, never swallowed.",
    ],
  },
  {
    title: "Licensing",
    items: [
      "Issue keys per project, machine or customer.",
      "Seat limits stop a key from spreading across a whole department.",
      "Optional expiry dates for trial or contract work.",
      "Suspend or revoke instantly; the effect is visible to the client on next check.",
      "Public validation endpoint for client-side or CI checks.",
    ],
  },
  {
    title: "Platform",
    items: [
      "Server-side quota enforcement, sized per plan.",
      "Retention windows that delete old jobs and their files automatically.",
      "Full job history with engine version, options and output list.",
      "Audit log for sign-ins, license changes and plan requests.",
      "Administrator view across users, licenses, messages and audit events.",
    ],
  },
];

export default function FeaturesPage() {
  return (
    <MarketingShell>
      <PageHeader
        eyebrow="Features"
        title="A conversion service, not a single web form"
        description="The hard part of shipping a ladder-to-TRISTATION service is everything around the conversion. That is what we built first."
      />

      <section className="mx-auto max-w-6xl px-5 py-16">
        <div className="grid gap-6 lg:grid-cols-3">
          {GROUPS.map((group) => (
            <Panel key={group.title} className="p-6">
              <h2 className="text-sm font-semibold text-mist-100">{group.title}</h2>
              <ul className="mt-4 space-y-3">
                {group.items.map((item) => (
                  <li key={item} className="flex gap-2.5 text-xs leading-relaxed text-mist-400">
                    <span className="mt-1.5 h-1 w-1 shrink-0 rounded-full bg-signal-400" />
                    {item}
                  </li>
                ))}
              </ul>
            </Panel>
          ))}
        </div>
      </section>

      <section className="border-t border-ink-800 bg-ink-900/30">
        <div className="mx-auto max-w-4xl px-5 py-20 text-center">
          <SectionHeading
            title="Built to be extended, not locked in"
            description="The conversion engine sits behind one HTTP contract. When your engine improves, the site gets better the next time you restart it."
          />
          <div className="mt-8 flex flex-wrap items-center justify-center gap-3">
            <Badge tone="info">POST /convert</Badge>
            <Badge tone="info">artifacts as base64 or URL</Badge>
            <Badge tone="info">warnings + engineVersion</Badge>
          </div>
          <div className="mt-8">
            <ButtonLink href="/how-it-works" size="lg">
              See the full flow
            </ButtonLink>
          </div>
        </div>
      </section>
    </MarketingShell>
  );
}
