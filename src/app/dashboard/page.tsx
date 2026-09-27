import Link from "next/link";
import { redirect } from "next/navigation";
import { getCurrentUser } from "@/lib/auth";
import { getDashboardContext } from "@/lib/dashboard";
import { listJobs } from "@/lib/jobs";
import { PageTitle, UsageBar } from "@/components/dashboard/page-title";
import { JobsTable } from "@/components/dashboard/job-bits";
import { Badge, ButtonLink, Panel, PanelTitle, Stat } from "@/components/ui";
import { formatBytes } from "@/lib/util";

export default async function DashboardPage() {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const context = await getDashboardContext(user);
  const recent = listJobs(user.id, 5);
  const limitLabel = Number.isFinite(context.plan.monthlyConversions)
    ? String(context.plan.monthlyConversions)
    : "∞";

  return (
    <div className="space-y-8">
      <PageTitle
        title={`Welcome back, ${user.name.split(" ")[0]}`}
        description="Your conversion activity, quota and licensing at a glance."
        action={
          <ButtonLink href="/dashboard/convert" size="sm">
            New conversion
          </ButtonLink>
        }
      />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Stat
          label="Conversions this month"
          value={`${context.usedThisMonth} / ${limitLabel}`}
          hint={Number.isFinite(context.remaining) ? `${context.remaining} remaining` : "Unlimited plan"}
        />
        <Stat
          label="Completed jobs"
          value={context.counts.completed}
          hint={`${context.counts.total} total in history`}
          tone="good"
        />
        <Stat
          label="Failed jobs"
          value={context.counts.failed}
          hint={context.counts.failed > 0 ? "Review the error on each job" : "No failures recorded"}
          tone={context.counts.failed > 0 ? "bad" : "neutral"}
        />
        <Stat
          label="Active licenses"
          value={context.licenses.filter((license) => license.status === "active").length}
          hint={`${context.plan.seats} seat${context.plan.seats === 1 ? "" : "s"} on ${context.plan.name}`}
          tone="info"
        />
      </div>

      <div className="grid gap-6 lg:grid-cols-[1.6fr_1fr]">
        <Panel>
          <PanelTitle
            title="Recent jobs"
            description="The five most recent conversion attempts."
            action={
              <Link
                href="/dashboard/jobs"
                className="text-xs text-signal-300 underline-offset-4 hover:underline"
              >
                View all
              </Link>
            }
          />
          <JobsTable jobs={recent} />
        </Panel>

        <div className="space-y-6">
          <Panel className="p-5">
            <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-mist-500">
              {context.plan.name} plan usage
            </p>
            <div className="mt-4">
              <UsageBar
                percent={context.usagePercent}
                label="Monthly conversions"
                value={`${context.usedThisMonth} of ${limitLabel}`}
              />
            </div>
            <dl className="mt-5 space-y-2.5 text-xs">
              {[
                ["Max file size", formatBytes(context.plan.maxUploadBytes)],
                ["Result retention", `${context.plan.retentionDays} days`],
                ["Batch uploads", context.plan.batchUploads ? "Included" : "Not available"],
                ["API access", context.plan.apiAccess ? "Included" : "Not available"],
              ].map(([label, value]) => (
                <div key={label} className="flex items-center justify-between">
                  <dt className="text-mist-500">{label}</dt>
                  <dd className="tabular-nums text-mist-300">{value}</dd>
                </div>
              ))}
            </dl>
            <div className="mt-5">
              <ButtonLink href="/dashboard/billing" variant="outline" size="sm" className="w-full">
                Compare plans
              </ButtonLink>
            </div>
          </Panel>

          <Panel className="p-5">
            <div className="flex items-center justify-between">
              <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-mist-500">
                Conversion engine
              </p>
              <Badge tone={context.engine.ready ? "good" : "warn"}>
                {context.engine.ready ? "Online" : "Not connected"}
              </Badge>
            </div>
            <p className="mt-3 text-sm text-mist-300">{context.engine.label}</p>
            <p className="mt-2 text-xs leading-relaxed text-mist-500">{context.engine.detail}</p>
          </Panel>
        </div>
      </div>
    </div>
  );
}
