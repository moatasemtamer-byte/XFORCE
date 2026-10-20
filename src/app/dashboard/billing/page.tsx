import Link from "next/link";
import { redirect } from "next/navigation";
import { getCurrentUser } from "@/lib/auth";
import { getDashboardContext } from "@/lib/dashboard";
import { requestPlanChangeAction } from "@/lib/actions/account-actions";
import { PageTitle } from "@/components/dashboard/page-title";
import { Badge, Button, Panel, PanelTitle, cx } from "@/components/ui";
import { PLANS, PLAN_ORDER } from "@/lib/licensing";
import { formatBytes, formatDate } from "@/lib/util";

export default async function BillingPage({ searchParams }: PageProps<"/dashboard/billing">) {
  const user = await getCurrentUser();
  if (!user) redirect("/login");
  // Billing is an admin surface now that accounts are created via admin-issued keys.
  if (user.role !== "admin") redirect("/dashboard");

  const context = await getDashboardContext(user);
  const query = await searchParams;
  const requested = typeof query.requested === "string" ? query.requested : null;

  return (
    <div className="space-y-8">
      <PageTitle
        title="Billing & plan"
        description="Your current allowance, and what changes if you move up a tier."
        action={
          <Badge tone="info">
            Current: {context.plan.name} · {user.role === "admin" ? "admin" : "member"}
          </Badge>
        }
      />

      {requested && PLANS[requested as keyof typeof PLANS] ? (
        <div className="rounded-[14px] border border-signal-500/40 bg-signal-500/10 p-5">
          <p className="text-sm font-semibold text-signal-300">Plan change requested</p>
          <p className="mt-2 text-xs leading-relaxed text-mist-300">
            Your request to move to{" "}
            <strong className="text-mist-100">{PLANS[requested as keyof typeof PLANS].name}</strong> has
            been recorded. We will confirm by email, then apply the new quota and issue the
            additional license keys included in that plan.
          </p>
        </div>
      ) : null}

      <div className="grid gap-6 lg:grid-cols-[1.6fr_1fr] lg:items-start">
        <Panel>
          <PanelTitle
            title="Plan comparison"
            description="Higher tiers raise monthly conversions, file size, seats and retention."
          />
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px] text-left text-sm">
              <thead>
                <tr className="border-b border-ink-700 text-[11px] uppercase tracking-[0.12em] text-mist-500">
                  <th className="px-5 py-3 font-medium">Plan</th>
                  <th className="px-5 py-3 font-medium">Monthly</th>
                  <th className="px-5 py-3 font-medium">Conversions</th>
                  <th className="px-5 py-3 font-medium">Max file</th>
                  <th className="px-5 py-3 font-medium">Seats</th>
                  <th className="px-5 py-3 font-medium">Retention</th>
                  <th className="px-5 py-3" />
                </tr>
              </thead>
              <tbody>
                {PLAN_ORDER.map((id) => {
                  const plan = PLANS[id];
                  const current = id === context.plan.id;
                  return (
                    <tr
                      key={id}
                      className={cx(
                        "border-b border-ink-800 last:border-0",
                        current ? "bg-signal-500/[0.06]" : "",
                      )}
                    >
                      <td className="px-5 py-3">
                        <span className="font-medium text-mist-100">{plan.name}</span>
                        {current ? <Badge tone="info" className="ml-2">Current</Badge> : null}
                      </td>
                      <td className="px-5 py-3 tabular-nums text-mist-300">
                        {plan.priceMonthly === null ? "Custom" : `$${plan.priceMonthly}`}
                      </td>
                      <td className="px-5 py-3 tabular-nums text-mist-300">
                        {Number.isFinite(plan.monthlyConversions) ? plan.monthlyConversions : "∞"}
                      </td>
                      <td className="px-5 py-3 tabular-nums text-mist-300">
                        {formatBytes(plan.maxUploadBytes)}
                      </td>
                      <td className="px-5 py-3 tabular-nums text-mist-300">{plan.seats}</td>
                      <td className="px-5 py-3 tabular-nums text-mist-300">{plan.retentionDays} d</td>
                      <td className="px-5 py-3 text-right">
                        {id === "enterprise" ? (
                          <Link
                            href="/contact"
                            className="text-xs text-signal-300 underline-offset-4 hover:underline"
                          >
                            Talk to us
                          </Link>
                        ) : current ? (
                          <span className="text-xs text-mist-500">Active</span>
                        ) : (
                          <form action={requestPlanChangeAction} className="inline">
                            <input type="hidden" name="plan" value={id} />
                            <Button type="submit" variant="outline" size="sm">
                              {PLAN_ORDER.indexOf(id) > PLAN_ORDER.indexOf(context.plan.id) ? "Upgrade" : "Switch"}
                            </Button>
                          </form>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </Panel>

        <div className="space-y-6">
          <Panel className="p-5">
            <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-mist-500">Account</p>
            <dl className="mt-4 space-y-2.5 text-xs">
              {[
                ["Account type", user.role === "admin" ? "Administrator" : "Standard"],
                ["Member since", formatDate(user.created_at)],
                ["Email", user.email],
                ["Billing contact", user.company ?? "—"],
              ].map(([label, value]) => (
                <div key={label} className="flex items-center justify-between gap-3">
                  <dt className="shrink-0 text-mist-500">{label}</dt>
                  <dd className="truncate text-right text-mist-300">{value}</dd>
                </div>
              ))}
            </dl>
          </Panel>

          <Panel className="p-5">
            <p className="text-sm font-semibold text-mist-100">How billing works</p>
            <p className="mt-2 text-xs leading-relaxed text-mist-400">
              Plan changes are approved by a person, not a payment button. We confirm the tier by
              email, then raise your quota and issue the license keys included in that plan. Annual
              pricing is 10 months for 12.
            </p>
            <div className="mt-4">
              <Link
                href="/pricing"
                className="text-xs text-signal-300 underline-offset-4 hover:underline"
              >
                Full pricing details
              </Link>
            </div>
          </Panel>
        </div>
      </div>
    </div>
  );
}
