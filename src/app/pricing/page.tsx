import type { Metadata } from "next";
import Link from "next/link";
import { MarketingShell, PageHeader } from "@/components/marketing-shell";
import { Badge, ButtonLink, Panel } from "@/components/ui";
import { PLANS, PLAN_ORDER } from "@/lib/licensing";
import { formatBytes } from "@/lib/util";

export const metadata: Metadata = {
  title: "Pricing",
  description: "Free, Starter, Professional and Enterprise plans for ladder diagram to MSW conversion.",
};

const FAQ = [
  {
    q: "What counts as one conversion?",
    a: "One uploaded source file that produces one job record, with all six artifacts attached. Jobs are uploaded and converted one file at a time.",
  },
  {
    q: "Do I need to pay before trying?",
    a: "No. The Free plan is permanent: five conversions a month, single files up to 5 MB, results kept for seven days. No card, no trial countdown.",
  },
  {
    q: "What is a license key for?",
    a: "A key records conversions against a named installation — a PLC workstation, a CI job, or a customer's machine. Keys carry a seat limit, an optional expiry date, and can be revoked instantly.",
  },
  {
    q: "Which files can I upload?",
    a: "A text-based ladder-logic PDF, or a raw monospace program listing such as a Triconex TRISTATION .LPT printout. CAD, image and database exports are not parsed — see the capabilities page for the full list.",
  },
  {
    q: "What happens to my uploaded files?",
    a: "They are stored under your account only, and deleted automatically once your retention window passes. Nothing is shared between accounts.",
  },
];

export default function PricingPage() {
  return (
    <MarketingShell>
      <PageHeader
        eyebrow="Pricing"
        title="Pay for volume, not for seats you do not use"
        description="Every plan includes the full web pipeline. The difference is quota, file size, licensing and retention."
      />

      <section className="mx-auto max-w-6xl px-5 py-16">
        <div className="grid gap-6 lg:grid-cols-4">
          {PLAN_ORDER.map((id) => {
            const plan = PLANS[id];
            const featured = id === "professional";
            return (
              <Panel key={id} className={featured ? "panel-raised glow-signal flex flex-col p-6" : "flex flex-col p-6"}>
                <div className="flex items-center justify-between">
                  <h2 className="text-base font-semibold">{plan.name}</h2>
                  {featured ? <Badge tone="info">Popular</Badge> : null}
                </div>

                <p className="mt-4 text-3xl font-semibold tabular-nums text-mist-100">
                  {plan.priceMonthly === null ? "Custom" : `$${plan.priceMonthly}`}
                  {plan.priceMonthly !== null ? (
                    <span className="text-sm font-normal text-mist-500"> / month</span>
                  ) : null}
                </p>
                {plan.priceYearly !== null && plan.priceMonthly !== 0 ? (
                  <p className="mt-1 text-xs text-mist-500">
                    or ${plan.priceYearly} yearly — two months free
                  </p>
                ) : null}

                <p className="mt-3 text-sm text-mist-400">{plan.tagline}</p>

                <dl className="mt-5 space-y-2 border-y border-ink-800 py-4 text-xs">
                  {[
                    [
                      "Conversions / month",
                      Number.isFinite(plan.monthlyConversions) ? String(plan.monthlyConversions) : "Unlimited",
                    ],
                    ["Max file size", formatBytes(plan.maxUploadBytes)],
                    ["Seats", String(plan.seats)],
                    ["License keys", String(plan.licenseKeys)],
                    ["Retention", `${plan.retentionDays} days`],
                    ["Queue", plan.priority],
                  ].map(([label, value]) => (
                    <div key={label} className="flex items-center justify-between gap-3">
                      <dt className="text-mist-500">{label}</dt>
                      <dd className="text-right tabular-nums text-mist-300">{value}</dd>
                    </div>
                  ))}
                </dl>

                <ul className="mt-5 flex-1 space-y-2.5 text-xs text-mist-400">
                  {plan.features.map((feature) => (
                    <li key={feature} className="flex gap-2.5">
                      <span className="mt-1.5 h-1 w-1 shrink-0 rounded-full bg-signal-400" />
                      {feature}
                    </li>
                  ))}
                </ul>

                <div className="mt-6">
                  {id === "enterprise" ? (
                    <ButtonLink href="/contact?source=enterprise" variant="outline" className="w-full">
                      Contact sales
                    </ButtonLink>
                  ) : (
                    <ButtonLink
                      href={`/checkout?plan=${id}`}
                      variant={featured ? "primary" : "outline"}
                      className="w-full"
                    >
                      {plan.priceMonthly === 0 ? "Start free" : "Get this license"}
                    </ButtonLink>
                  )}
                </div>
              </Panel>
            );
          })}
        </div>

        <p className="mt-8 text-center text-xs text-mist-500">
          Prices exclude tax. Annual billing is available on every paid plan. Checkout in this build uses a mock
          payment provider: no card is collected and the license is generated immediately.{" "}
          <Link href="/contact" className="text-signal-300 underline-offset-4 hover:underline">
            Ask about volume pricing
          </Link>
          .
        </p>
      </section>

      <section className="border-t border-ink-800 bg-ink-900/30">
        <div className="mx-auto max-w-3xl px-5 py-16">
          <h2 className="text-2xl font-semibold">Questions engineers actually ask</h2>
          <dl className="mt-8 space-y-6">
            {FAQ.map((item) => (
              <div key={item.q}>
                <dt className="text-sm font-semibold text-mist-100">{item.q}</dt>
                <dd className="mt-2 text-sm leading-relaxed text-mist-400">{item.a}</dd>
              </div>
            ))}
          </dl>
        </div>
      </section>
    </MarketingShell>
  );
}
