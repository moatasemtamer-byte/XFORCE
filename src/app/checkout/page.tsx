import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { checkoutAction } from "@/lib/actions/access-actions";
import { checkoutErrorMessage } from "@/lib/access-messages";
import { mockBillingEnabled, mockBillingToken } from "@/lib/env";
import { PLANS, PLAN_ORDER } from "@/lib/licensing";
import { FunnelShell, LightField, Notice, lightInputClass } from "@/components/landing/funnel-shell";
import { ActionButton, ActionLink, MicroLabel } from "@/components/landing/primitives";
import { formatBytes } from "@/lib/util";
import type { PlanId } from "@/lib/types";

export const metadata: Metadata = {
  title: "Get a license",
  description: "Choose a plan and a license is generated for your account immediately.",
};

const SELF_SERVE: PlanId[] = PLAN_ORDER.filter((id) => PLANS[id].priceMonthly !== null);

/** Self-serve plans plus a short, honest feature list. */
function featuresFor(plan: PlanId): string[] {
  return PLANS[plan].features.slice(0, 5);
}

export default async function CheckoutPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const params = await searchParams;
  const error = checkoutErrorMessage(typeof params.error === "string" ? params.error : undefined);

  const planRaw = typeof params.plan === "string" ? params.plan : "";
  const plan = (SELF_SERVE as string[]).includes(planRaw) ? (planRaw as PlanId) : null;

  // Enterprise and anything unrecognised go to a human.
  if (planRaw && !plan) redirect(planRaw === "enterprise" ? "/contact?source=enterprise" : "/pricing");

  const cycle = params.cycle === "yearly" ? "yearly" : "monthly";
  const token = mockBillingToken();
  const available = mockBillingEnabled();

  const definition = plan ? PLANS[plan] : null;
  const amount = definition
    ? cycle === "yearly"
      ? definition.priceYearly
      : definition.priceMonthly
    : null;

  return (
    <FunnelShell
      eyebrow="Get a license"
      title={plan ? `${definition?.name} license` : "Choose a license"}
      lede={
        plan
          ? "No card is collected. This build uses a mock payment provider, so the license below is a real license you can use immediately — it is simply not paid for."
          : "Pick the plan that matches your volume. A license is generated for your account as soon as checkout completes."
      }
      aside={
        <div className="space-y-8">
          {!available ? (
            <Notice tone="info">
              Checkout is disabled on this server. Set <code className="font-mono">MOCK_BILLING_TOKEN</code> in the
              environment to enable the mock provider, or use the contact form.
            </Notice>
          ) : (
            <Notice tone="info">
              <strong className="font-semibold">Mock payment provider.</strong> No card details are requested and no
              charge is made. Treat any license bought here as a test license.
            </Notice>
          )}

          <div>
            <MicroLabel>Other plans</MicroLabel>
            <ul className="mt-4 space-y-2">
              {SELF_SERVE.filter((id) => id !== plan).map((id) => (
                <li key={id}>
                  <a
                    href={`/checkout?plan=${id}&cycle=${cycle}`}
                    className="flex items-center justify-between border border-hair px-4 py-3 text-sm text-ink transition-colors hover:border-ink focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-brand-800"
                  >
                    <span>{PLANS[id].name}</span>
                    <span className="font-mono text-xs tabular-nums text-ink-3">
                      {PLANS[id].priceMonthly === 0 ? "Free" : `$${PLANS[id].priceMonthly}`}
                      <span className="text-ink-4">/mo</span>
                    </span>
                  </a>
                </li>
              ))}
              <li>
                <a
                  href="/contact?source=enterprise"
                  className="flex items-center justify-between border border-hair px-4 py-3 text-sm text-ink transition-colors hover:border-ink focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-brand-800"
                >
                  <span>Enterprise</span>
                  <span className="font-mono text-xs text-ink-3">Contact us</span>
                </a>
              </li>
            </ul>
          </div>

          <div className="border border-hair bg-paper-2 p-6">
            <MicroLabel>Already bought?</MicroLabel>
            <p className="mt-3 text-sm leading-relaxed text-ink-2">Enter the key you already have instead.</p>
            <div className="mt-5">
              <ActionLink href="/enter-license" tone="outline" size="md">
                Enter a license
              </ActionLink>
            </div>
          </div>
        </div>
      }
    >
      {plan && definition ? (
        <div className="max-w-xl">
          {error ? (
            <div className="mb-6">
              <Notice tone="bad">{error}</Notice>
            </div>
          ) : null}

          <div className="border border-hair">
            <div className="flex items-start justify-between gap-4 border-b border-hair p-6">
              <div>
                <h2 className="text-lg font-semibold text-ink">{definition.name}</h2>
                <p className="mt-1 text-sm text-ink-2">{definition.tagline}</p>
              </div>
              <p className="shrink-0 text-right">
                <span className="block text-2xl font-semibold tabular-nums text-ink">
                  {amount === 0 ? "Free" : `$${amount}`}
                </span>
                {amount !== 0 ? (
                  <span className="text-[11px] text-ink-3">{cycle === "yearly" ? "per year" : "per month"}</span>
                ) : null}
              </p>
            </div>

            <dl className="grid grid-cols-2 gap-px bg-hair sm:grid-cols-4">
              {[
                ["Conversions", Number.isFinite(definition.monthlyConversions) ? String(definition.monthlyConversions) : "Unlimited"],
                ["Max file", formatBytes(definition.maxUploadBytes)],
                ["Seats", String(definition.seats)],
                ["Retention", `${definition.retentionDays} days`],
              ].map(([label, value]) => (
                <div key={label} className="bg-paper px-4 py-3">
                  <dt className="text-[10px] uppercase tracking-[0.14em] text-ink-3">{label}</dt>
                  <dd className="mt-1 text-sm font-medium tabular-nums text-ink">{value}</dd>
                </div>
              ))}
            </dl>

            <ul className="space-y-2 border-t border-hair p-6">
              {featuresFor(plan).map((feature) => (
                <li key={feature} className="flex items-start gap-2.5 text-sm text-ink-2">
                  <span aria-hidden="true" className="mt-[7px] h-1.5 w-1.5 shrink-0 bg-brand-800" />
                  {feature}
                </li>
              ))}
            </ul>
          </div>

          <form action={checkoutAction} className="mt-8 space-y-5">
            <input type="hidden" name="plan" value={plan} />
            <input type="hidden" name="cycle" value={cycle} />

            <LightField label="Email" htmlFor="email" hint="Your license is bound to this address. It is also the name of your seat.">
              <input
                id="email"
                name="email"
                type="email"
                required
                autoComplete="email"
                placeholder="you@company.com"
                className={lightInputClass}
              />
            </LightField>

            {token ? (
              <LightField
                label="Mock checkout key"
                htmlFor="token"
                hint="Required while the payment provider is mocked. In production this form is replaced by the provider's hosted page."
              >
                <input
                  id="token"
                  name="token"
                  required
                  autoComplete="off"
                  spellCheck={false}
                  placeholder="Paste MOCK_BILLING_TOKEN"
                  className={`${lightInputClass} font-mono`}
                />
              </LightField>
            ) : null}

            <div className="flex flex-wrap items-center gap-4">
              <ActionButton type="submit" size="lg" disabled={!available} className="w-full sm:w-auto">
                {amount === 0 ? "Create free license" : `Pay $${amount} and generate license`}
              </ActionButton>
              <ActionLink href="/pricing" tone="outline" size="lg">
                Change plan
              </ActionLink>
            </div>

            {!available ? (
              <p className="text-[11px] leading-relaxed text-ink-3">
                The button is disabled because no mock checkout key is configured on this server.
              </p>
            ) : null}
          </form>
        </div>
      ) : (
        <div className="max-w-xl">
          <Notice tone="info">Choose a plan to continue.</Notice>
          <ul className="mt-6 space-y-2">
            {SELF_SERVE.map((id) => (
              <li key={id}>
                <ActionLink href={`/checkout?plan=${id}`} tone="outline" size="md" className="w-full justify-between">
                  <span>{PLANS[id].name}</span>
                  <span className="font-mono text-xs tabular-nums text-ink-3">
                    {PLANS[id].priceMonthly === 0 ? "Free" : `$${PLANS[id].priceMonthly}/mo`}
                  </span>
                </ActionLink>
              </li>
            ))}
          </ul>
        </div>
      )}
    </FunnelShell>
  );
}
