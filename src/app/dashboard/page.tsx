import Link from "next/link";
import { redirect } from "next/navigation";
import { getCurrentUser } from "@/lib/auth";
import { getDashboardContext } from "@/lib/dashboard";
import { dashboardAnalytics } from "@/lib/analytics";
import { listJobs } from "@/lib/jobs";
import { PageTitle, UsageBar } from "@/components/dashboard/page-title";
import { JobsTable } from "@/components/dashboard/job-bits";
import { Badge, ButtonLink, Panel, PanelTitle, Stat } from "@/components/ui";
import { formatBytes, formatDateTime } from "@/lib/util";

function shortDate(key: string): string {
  const [, month, day] = key.split("-").map(Number);
  const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
  return `${day} ${months[month - 1] ?? ""}`.trim();
}

export default async function DashboardPage() {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const context = await getDashboardContext(user);
  const analytics = dashboardAnalytics(user.id, context.plan.retentionDays);
  const recent = listJobs(user.id, 5);
  const limitLabel = Number.isFinite(context.plan.monthlyConversions)
    ? String(context.plan.monthlyConversions)
    : "∞";

  const peak = Math.max(1, ...analytics.activity.map((point) => point.total));
  const successPercent = Math.round(analytics.successRate * 100);

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
        <Panel className="p-5">
          <div className="flex items-center justify-between">
            <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-mist-500">
              Conversions over time
            </p>
            <div className="flex items-center gap-3 text-[11px] text-mist-500">
              <span className="flex items-center gap-1.5">
                <span className="h-2 w-2 rounded-sm bg-signal-400" /> completed
              </span>
              <span className="flex items-center gap-1.5">
                <span className="h-2 w-2 rounded-sm bg-bad-500" /> failed
              </span>
            </div>
          </div>
          <div className="mt-5 flex h-40 items-end gap-1.5">
            {analytics.activity.map((point) => {
              const completedH = Math.round((point.completed / peak) * 100);
              const failedH = Math.round((point.failed / peak) * 100);
              return (
                <div key={point.day} className="group flex flex-1 flex-col items-center justify-end gap-0.5">
                  <div className="flex h-full w-full flex-col items-center justify-end gap-0.5">
                    {point.failed > 0 ? (
                      <div
                        className="w-full rounded-t-sm bg-bad-500/80"
                        style={{ height: `${failedH}%` }}
                        title={`${point.failed} failed`}
                      />
                    ) : null}
                    {point.completed > 0 ? (
                      <div
                        className={point.failed > 0 ? "w-full bg-signal-400" : "w-full rounded-t-sm bg-signal-400"}
                        style={{ height: `${completedH}%` }}
                        title={`${point.completed} completed`}
                      />
                    ) : null}
                    {point.total === 0 ? (
                      <div className="w-full rounded-t-sm bg-ink-800" style={{ height: "2px" }} />
                    ) : null}
                  </div>
                </div>
              );
            })}
          </div>
          <div className="mt-2 flex justify-between text-[10px] tabular-nums text-mist-600">
            <span>{analytics.activity.length > 0 ? shortDate(analytics.activity[0].day) : ""}</span>
            <span>
              {analytics.activity.length > 0
                ? shortDate(analytics.activity[analytics.activity.length - 1].day)
                : ""}
            </span>
          </div>
        </Panel>

        <div className="space-y-6">
          <Panel className="p-5">
            <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-mist-500">
              Success rate
            </p>
            <div className="mt-4 flex items-baseline gap-2">
              <span className="text-3xl font-semibold tabular-nums text-mist-50">{successPercent}%</span>
              <span className="text-xs text-mist-500">
                {analytics.completed} of {analytics.completed + analytics.failed} finished
              </span>
            </div>
            <div className="mt-3 h-2 overflow-hidden rounded-full bg-ink-800">
              <div
                className={
                  successPercent >= 90
                    ? "h-full rounded-full bg-good-500"
                    : successPercent >= 60
                      ? "h-full rounded-full bg-amberline-500"
                      : "h-full rounded-full bg-bad-500"
                }
                style={{ width: `${successPercent}%` }}
              />
            </div>
          </Panel>

          <Panel className="p-5">
            <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-mist-500">
              Source formats
            </p>
            {analytics.formats.length === 0 ? (
              <p className="mt-3 text-xs text-mist-500">No conversions yet.</p>
            ) : (
              <ul className="mt-4 space-y-3">
                {analytics.formats.map((slice) => (
                  <li key={slice.family}>
                    <div className="flex items-center justify-between text-xs">
                      <span className="text-mist-300">{slice.family}</span>
                      <span className="tabular-nums text-mist-500">{slice.count}</span>
                    </div>
                    <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-ink-800">
                      <div
                        className="h-full rounded-full bg-signal-400"
                        style={{ width: `${Math.max(4, Math.round(slice.share * 100))}%` }}
                      />
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </Panel>
        </div>
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
                ["Stored on disk", formatBytes(analytics.retention.storedBytes)],
                ["Source formats", "PDF or listing"],
              ].map(([label, value]) => (
                <div key={label} className="flex items-center justify-between">
                  <dt className="text-mist-500">{label}</dt>
                  <dd className="tabular-nums text-mist-300">{value}</dd>
                </div>
              ))}
            </dl>
            {analytics.retention.daysUntilOldestPurge !== null && analytics.total > 0 ? (
              <p className="mt-3 rounded-lg border border-ink-700 bg-ink-900/50 px-3 py-2.5 text-[11px] leading-relaxed text-mist-400">
                Your oldest result is purged in{" "}
                <span className="text-mist-200">{analytics.retention.daysUntilOldestPurge} day(s)</span>
                {analytics.retention.oldestJobAt
                  ? ` (${formatDateTime(analytics.retention.oldestJobAt).slice(0, 10)})`
                  : ""}
                .
                {analytics.retention.expiringSoon > 0
                  ? ` ${analytics.retention.expiringSoon} job(s) expire within a week.`
                  : ""}
              </p>
            ) : null}
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

          <Panel className="p-5">
            <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-mist-500">
              Your capabilities
            </p>
            {context.features.length === 0 ? (
              <p className="mt-3 rounded-lg border border-bad-500/40 bg-bad-500/10 px-3 py-2.5 text-[11px] leading-relaxed text-bad-400">
                No capabilities are enabled on your account yet. Ask your administrator to grant the
                features you need.
              </p>
            ) : (
              <div className="mt-3 flex flex-wrap gap-1.5">
                {context.features.map((feature) => (
                  <span
                    key={feature}
                    className="rounded border border-signal-400/30 bg-signal-400/10 px-2 py-0.5 font-mono text-[10px] text-signal-200"
                  >
                    {feature}
                  </span>
                ))}
              </div>
            )}
            <div className="mt-4">
              <ButtonLink href="/dashboard/licenses" variant="outline" size="sm" className="w-full">
                Manage licenses
              </ButtonLink>
            </div>
          </Panel>
        </div>
      </div>
    </div>
  );
}
