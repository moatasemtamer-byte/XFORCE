import Link from "next/link";
import { MarketingShell } from "@/components/marketing-shell";
import { Badge, ButtonLink, Panel, SectionHeading } from "@/components/ui";
import { LadderPreview } from "@/components/ladder-preview";
import { engineStatus } from "@/lib/converter";
import { PLANS, PLAN_ORDER } from "@/lib/licensing";

const STEPS = [
  {
    title: "Upload the drawing",
    body: "Drop in the ladder diagram export or a scan. We check format, size and plan quota before anything is queued.",
    icon: "M12 16V4m0 12l-4-4m4 4l4-4M4 20h16",
  },
  {
    title: "Engine converts to MSW",
    body: "The conversion engine parses rungs, contacts and coils, then emits the MSW deliverable plus optional netlist and report.",
    icon: "M4 7h16M4 12h10M4 17h7M17 15l3 2-3 2",
  },
  {
    title: "Download and ship",
    body: "Results land in job history with a signed job id, ready to hand to the mechanical or controls team.",
    icon: "M12 4v12m0 0l-4-4m4 4l4-4M4 20h16",
  },
];

const FEATURES = [
  {
    title: "Ladder-aware parsing",
    body: "Contacts, coils, timers, counters and branch structure are read as logic, not as ink. Rung numbering is preserved end to end.",
  },
  {
    title: "MSW output contract",
    body: "A documented adapter contract so your engine stays yours. We handle auth, quota, storage, retention and audit around it.",
  },
  {
    title: "Licensing that engineers can use",
    body: "Issue keys with seat limits, expiry dates and revocation. Validate them from the dashboard or the public API.",
  },
  {
    title: "Auditable job history",
    body: "Every job records source, options, engine version, warnings and outputs, with timestamps you can hand to QA.",
  },
  {
    title: "Plan-based quotas",
    body: "Monthly conversion allowances, per-file size caps and retention windows enforced server side, not in the browser.",
  },
  {
    title: "API access",
    body: "Professional and Enterprise plans get a REST endpoint for uploading sources and polling results from your own tooling.",
  },
];

const PORTFOLIO = [
  { tag: "Conveyor interlocks", metric: "1,240 rungs", detail: "Batched motor starter schematic converted and re-checked against the original." },
  { tag: "Pump station panel", metric: "6 min", detail: "Full IO list and rung report produced from a single uploaded sheet." },
  { tag: "Packaging line", metric: "38 outputs", detail: "Netlist, BOM and preview generated in a single pass." },
];

export default async function Home() {
  const engine = await engineStatus();
  const previewPlans = PLAN_ORDER.filter((id) => id !== "enterprise");

  return (
    <MarketingShell>
      <section className="relative overflow-hidden border-b border-ink-800">
        <div className="blueprint absolute inset-0 opacity-70" />
        <div className="absolute inset-0 bg-gradient-to-b from-ink-950/40 via-ink-950/80 to-ink-950" />

        <div className="relative mx-auto grid max-w-6xl gap-12 px-5 py-20 lg:grid-cols-[1.05fr_0.95fr] lg:items-center lg:py-28">
          <div>
            <Badge tone={engine.ready ? "good" : "warn"}>
              <span className={engine.ready ? "h-1.5 w-1.5 rounded-full bg-good-500" : "h-1.5 w-1.5 rounded-full bg-amberline-400"} />
              {engine.ready ? "Conversion engine online" : "Engine integration pending"}
            </Badge>

            <h1 className="mt-6 text-4xl font-semibold leading-[1.08] sm:text-5xl lg:text-[3.4rem]">
              Ladder diagrams in.
              <span className="text-gradient block">MSW deliverables out.</span>
            </h1>

            <p className="mt-6 max-w-xl text-[17px] leading-relaxed text-mist-400">
              XforceMsw is the company we built to do one job properly: convert PLC ladder diagrams
              into MSW. Upload once, get a validated conversion with a full audit trail, and manage
              every seat and license from the same dashboard.
            </p>

            <div className="mt-8 flex flex-wrap items-center gap-3">
              <ButtonLink href="/register" size="lg">
                Start free — 5 conversions
              </ButtonLink>
              <ButtonLink href="/how-it-works" variant="outline" size="lg">
                See how it works
              </ButtonLink>
            </div>

            <dl className="mt-10 grid max-w-lg grid-cols-3 gap-6 border-t border-ink-800 pt-6">
              {[
                ["100%", "Rung traceable"],
                ["< 10 min", "Typical turnaround"],
                ["REST", "API on Pro plans"],
              ].map(([value, label]) => (
                <div key={label}>
                  <dt className="text-lg font-semibold text-mist-100">{value}</dt>
                  <dd className="mt-0.5 text-xs text-mist-500">{label}</dd>
                </div>
              ))}
            </dl>
          </div>

          <div className="relative">
            <div className="panel-raised glow-signal overflow-hidden p-4">
              <div className="flex items-center justify-between px-1 pb-3">
                <p className="font-mono text-[11px] text-mist-500">SOURCE / MOTOR_STARTERS.SCH</p>
                <Badge tone="info">queued</Badge>
              </div>
              <LadderPreview className="w-full" />
              <div className="mt-4 space-y-2 border-t border-ink-700 px-1 pt-4 font-mono text-[11px]">
                <p className="text-mist-500">
                  <span className="text-signal-400">&gt;</span> parsing rungs…
                </p>
                <p className="text-mist-500">
                  <span className="text-signal-400">&gt;</span> 4 rungs, 3 NO, 1 NC
                </p>
                <p className="text-mist-500">
                  <span className="text-signal-400">&gt;</span> writing MSW bundle
                  <span className="ml-1 inline-block h-3 w-1.5 bg-signal-400 align-middle" />
                </p>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="border-b border-ink-800 bg-ink-900/30">
        <div className="mx-auto max-w-6xl px-5 py-14">
          <SectionHeading
            eyebrow="How it works"
            title="Three steps, no CAD licence required"
            description="The browser never touches the conversion. Everything runs server side so a dropped connection never loses a job."
          />
          <div className="mt-12 grid gap-6 md:grid-cols-3">
            {STEPS.map((step, index) => (
              <Panel key={step.title} className="p-6">
                <div className="flex items-center justify-between">
                  <span className="grid h-9 w-9 place-items-center rounded-lg border border-ink-600 bg-ink-800 text-signal-400">
                    <svg viewBox="0 0 24 24" className="h-4.5 w-4.5" fill="none" stroke="currentColor" strokeWidth="1.7">
                      <path d={step.icon} strokeLinecap="round" strokeLinejoin="round" />
                    </svg>
                  </span>
                  <span className="font-mono text-xs text-ink-400">0{index + 1}</span>
                </div>
                <h3 className="mt-5 text-base font-semibold">{step.title}</h3>
                <p className="mt-2 text-sm leading-relaxed text-mist-400">{step.body}</p>
              </Panel>
            ))}
          </div>
        </div>
      </section>

      <section className="border-b border-ink-800">
        <div className="mx-auto max-w-6xl px-5 py-20">
          <SectionHeading
            eyebrow="Platform"
            title="Everything around the conversion is already built"
            description="We focused on the parts that make a conversion service usable inside a real controls department."
          />
          <div className="mt-12 grid gap-px overflow-hidden rounded-[14px] border border-ink-700 bg-ink-700 sm:grid-cols-2 lg:grid-cols-3">
            {FEATURES.map((feature) => (
              <div key={feature.title} className="bg-ink-900 p-6">
                <h3 className="text-sm font-semibold text-mist-100">{feature.title}</h3>
                <p className="mt-2 text-sm leading-relaxed text-mist-400">{feature.body}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section className="border-b border-ink-800 bg-ink-900/30">
        <div className="mx-auto max-w-6xl px-5 py-20">
          <div className="grid gap-10 lg:grid-cols-[1fr_1fr] lg:items-center">
            <div>
              <SectionHeading
                align="left"
                eyebrow="Engine contract"
                title="Your engine stays yours"
                description="The web platform never reimplements your conversion logic. It calls one documented HTTP endpoint, stores what comes back, and enforces licensing around it."
              />
              <ul className="mt-8 space-y-3 text-sm text-mist-400">
                {[
                  "POST /convert with multipart source file and options",
                  "Returns artifacts as base64 or URL, plus warnings and summary",
                  "Engine version recorded on every job for traceability",
                  "Swap engines by changing MSW_ENGINE_URL — no redeploy of the site",
                ].map((line) => (
                  <li key={line} className="flex gap-3">
                    <span className="mt-2 h-1 w-1 shrink-0 rounded-full bg-signal-400" />
                    {line}
                  </li>
                ))}
              </ul>
              <div className="mt-8">
                <Badge tone={engine.ready ? "good" : "warn"}>
                  {engine.ready ? `Connected: ${engine.label}` : "Not connected yet"}
                </Badge>
                <p className="mt-3 max-w-md text-xs leading-relaxed text-mist-500">{engine.detail}</p>
              </div>
            </div>

            <Panel className="overflow-hidden">
              <div className="border-b border-ink-700 bg-ink-850 px-4 py-2.5 font-mono text-[11px] text-mist-500">
                engine contract
              </div>
              <pre className="overflow-x-auto px-4 py-4 font-mono text-[11.5px] leading-relaxed text-mist-300">
{`POST {MSW_ENGINE_URL}/convert
Content-Type: multipart/form-data
Authorization: Bearer {MSW_ENGINE_TOKEN}

  file        ladder-diagram export
  jobId       xmsw job identifier
  options     { targetFormat, includeNetlist, … }
  licenseKey  issued key, for engine-side checks

200 OK
{
  "artifacts": [
    { "name": "panel.msw",
      "mimeType": "application/octet-stream",
      "kind": "primary",
      "contentBase64": "TVSW…" }
  ],
  "warnings": [],
  "summary": { "rungs": 4 },
  "engineVersion": "2.3.1"
}`}
              </pre>
            </Panel>
          </div>
        </div>
      </section>

      <section className="border-b border-ink-800">
        <div className="mx-auto max-w-6xl px-5 py-20">
          <SectionHeading
            eyebrow="Selected work"
            title="What teams have shipped through the pipeline"
          />
          <div className="mt-12 grid gap-6 md:grid-cols-3">
            {PORTFOLIO.map((item) => (
              <Panel key={item.tag} className="p-6">
                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-signal-400">
                  {item.tag}
                </p>
                <p className="mt-3 text-2xl font-semibold tabular-nums text-mist-100">{item.metric}</p>
                <p className="mt-2 text-sm leading-relaxed text-mist-400">{item.detail}</p>
              </Panel>
            ))}
          </div>
          <div className="mt-8 text-center">
            <Link
              href="/portfolio"
              className="text-sm text-signal-300 underline-offset-4 hover:underline"
            >
              Read the full portfolio
            </Link>
          </div>
        </div>
      </section>

      <section className="bg-ink-900/30">
        <div className="mx-auto max-w-6xl px-5 py-20">
          <SectionHeading
            eyebrow="Pricing"
            title="Start free, scale when the drawings do"
            description="Every plan includes the full web pipeline. Higher plans raise quotas, file sizes, seats and retention."
          />
          <div className="mt-12 grid gap-6 md:grid-cols-3">
            {previewPlans.map((id) => {
              const plan = PLANS[id];
              const featured = id === "professional";
              return (
                <Panel
                  key={id}
                  className={featured ? "panel-raised glow-signal p-6" : "p-6"}
                >
                  <div className="flex items-center justify-between">
                    <h3 className="text-base font-semibold">{plan.name}</h3>
                    {featured ? <Badge tone="info">Popular</Badge> : null}
                  </div>
                  <p className="mt-4 text-3xl font-semibold tabular-nums text-mist-100">
                    ${plan.priceMonthly}
                    <span className="text-sm font-normal text-mist-500"> / month</span>
                  </p>
                  <p className="mt-2 text-sm text-mist-400">{plan.tagline}</p>
                  <ul className="mt-6 space-y-2.5 text-sm text-mist-400">
                    {plan.features.slice(0, 4).map((feature) => (
                      <li key={feature} className="flex gap-2.5">
                        <span className="mt-1.5 h-1 w-1 shrink-0 rounded-full bg-signal-400" />
                        {feature}
                      </li>
                    ))}
                  </ul>
                  <div className="mt-6">
                    <ButtonLink
                      href="/register"
                      variant={featured ? "primary" : "outline"}
                      className="w-full"
                    >
                      {plan.priceMonthly === 0 ? "Start free" : "Choose plan"}
                    </ButtonLink>
                  </div>
                </Panel>
              );
            })}
          </div>
        </div>
      </section>

      <section className="border-t border-ink-800">
        <div className="mx-auto max-w-4xl px-5 py-20 text-center">
          <h2 className="text-3xl font-semibold">Ready to move drawings through?</h2>
          <p className="mx-auto mt-4 max-w-xl text-[15px] text-mist-400">
            Create an account, upload a ladder diagram, and you will have your first MSW conversion
            job in under a minute.
          </p>
          <div className="mt-8 flex flex-wrap justify-center gap-3">
            <ButtonLink href="/register" size="lg">
              Create free account
            </ButtonLink>
            <ButtonLink href="/contact" variant="outline" size="lg">
              Talk to us
            </ButtonLink>
          </div>
        </div>
      </section>
    </MarketingShell>
  );
}
