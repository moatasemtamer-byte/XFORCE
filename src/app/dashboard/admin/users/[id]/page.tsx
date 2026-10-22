import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getCurrentUser } from "@/lib/auth";
import { getUserForAdmin, listJobsForAdmin } from "@/lib/jobs";
import { listLicensesForAdmin, listSeats, type LicenseSeat } from "@/lib/licensing";
import { PageTitle } from "@/components/dashboard/page-title";
import { Badge, ButtonLink, EmptyState, Panel, PanelTitle, Stat, cx } from "@/components/ui";
import { formatBytes, formatDateTime } from "@/lib/util";

const TARGET_LABELS: Record<string, string> = {
  tia: "TIA package",
  pt2x: "PT2X",
  xml: "TriStation XML",
  msw: "TIA package",
};

const STATUS_TONE: Record<string, "good" | "bad" | "warn" | "neutral" | "info"> = {
  completed: "good",
  failed: "bad",
  processing: "info",
  queued: "warn",
};

export default async function AdminUserPage({ params }: PageProps<"/dashboard/admin/users/[id]">) {
  const admin = await getCurrentUser();
  if (!admin) redirect("/login");
  if (admin.role !== "admin") redirect("/dashboard");

  const { id } = await params;
  const account = getUserForAdmin(id);
  if (!account) notFound();

  const jobs = listJobsForAdmin(id);
  const licenses = listLicensesForAdmin(500).filter((license) => license.user_id === id);
  const seatsByLicense: Record<string, LicenseSeat[]> = {};
  for (const license of licenses) seatsByLicense[license.id] = listSeats(license.id);

  return (
    <div className="space-y-8">
      <PageTitle
        title={account.name || account.email}
        description={`${account.email} · joined ${formatDateTime(account.created_at).slice(0, 10)}`}
        action={
          <div className="flex gap-2">
            <ButtonLink href="/dashboard/admin" variant="outline" size="sm">
              All users
            </ButtonLink>
            <Badge tone={account.role === "admin" ? "bad" : "neutral"}>{account.role}</Badge>
          </div>
        }
      />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Stat label="Conversions" value={account.jobs} />
        <Stat label="Completed" value={account.completed} tone="info" />
        <Stat label="Failed" value={account.failed} />
        <Stat label="Uploaded volume" value={formatBytes(account.bytes)} />
      </div>

      <Panel className="p-5">
        <dl className="grid gap-4 text-xs sm:grid-cols-4">
          <div>
            <dt className="text-mist-500">Company</dt>
            <dd className="mt-1 text-mist-300">{account.company ?? "—"}</dd>
          </div>
          <div>
            <dt className="text-mist-500">Plan</dt>
            <dd className="mt-1 text-mist-300">{account.plan}</dd>
          </div>
          <div>
            <dt className="text-mist-500">Sign-in</dt>
            <dd className="mt-1 text-mist-300">
              {account.passwordless ? "License key (no password)" : "Email & password"}
            </dd>
          </div>
          <div>
            <dt className="text-mist-500">Licenses</dt>
            <dd className="mt-1 text-mist-300">{licenses.length}</dd>
          </div>
        </dl>
      </Panel>

      {licenses.length > 0 ? (
        <Panel>
          <PanelTitle title="Licenses" description="Keys tied to this account and who is holding their seats." />
          <ul className="divide-y divide-ink-800">
            {licenses.map((license) => {
              const seats = seatsByLicense[license.id] ?? [];
              return (
                <li key={license.id} className="px-5 py-3.5">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-mono text-xs text-mist-200">{license.key}</span>
                    <Badge tone="info">{license.plan}</Badge>
                    <Badge tone={license.expired ? "warn" : "good"}>
                      {license.expired ? "expired" : license.status}
                    </Badge>
                    <span className="ml-auto text-[11px] tabular-nums text-mist-500">
                      {seats.length}/{license.activation_limit} seats
                    </span>
                  </div>
                  {seats.length > 0 ? (
                    <p className="mt-1.5 text-[11px] text-mist-500">
                      Used by {seats.map((seat) => seat.holder).join(", ")}
                    </p>
                  ) : null}
                </li>
              );
            })}
          </ul>
        </Panel>
      ) : null}

      <Panel>
        <PanelTitle
          title="Uploads & conversions"
          description={`${jobs.length} job${jobs.length === 1 ? "" : "s"} — source file, target and downloadable outputs.`}
        />
        {jobs.length === 0 ? (
          <EmptyState
            title="No conversions yet"
            description="This account has not uploaded anything. Issue it a license so it can start."
          />
        ) : (
          <ul className="divide-y divide-ink-800">
            {jobs.map((job) => {
              const targetRaw = typeof job.options.target === "string" ? job.options.target : "tia";
              const targetLabel = TARGET_LABELS[targetRaw] ?? targetRaw;
              return (
                <li key={job.id} className="px-5 py-4">
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge tone={STATUS_TONE[job.status] ?? "neutral"}>{job.status}</Badge>
                    <p className="truncate font-mono text-xs text-mist-200">{job.source_name}</p>
                    <span className="text-[11px] text-mist-500">{formatBytes(job.source_size)}</span>
                    <span className="ml-auto text-[11px] text-mist-500">
                      {formatDateTime(job.created_at)}
                    </span>
                  </div>
                  <div className="mt-1.5 flex flex-wrap items-center gap-x-4 gap-y-1 text-[11px] text-mist-500">
                    <span>
                      Output: <span className="text-mist-300">{targetLabel}</span>
                    </span>
                    <span>
                      Engine: <span className="font-mono">{job.engine}</span>
                    </span>
                    {job.finished_at ? <span>Finished {formatDateTime(job.finished_at)}</span> : null}
                  </div>

                  {job.error ? (
                    <p className="mt-2 rounded-lg border border-bad-500/40 bg-bad-500/10 px-3 py-2 text-[11px] leading-relaxed text-bad-500">
                      {job.error}
                    </p>
                  ) : null}

                  {job.outputs.length > 0 ? (
                    <div className="mt-3 flex flex-wrap gap-2">
                      {job.outputs.map((output, index) => (
                        <a
                          key={output.name}
                          href={`/api/admin/jobs/${job.id}/output/${index}`}
                          className={cx(
                            "inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1.5 text-[11px] font-medium transition-colors",
                            output.kind === "primary"
                              ? "border-signal-500/50 text-signal-200 hover:bg-signal-500/10"
                              : "border-ink-600 text-mist-300 hover:border-signal-500/50 hover:text-mist-100",
                          )}
                        >
                          <svg viewBox="0 0 24 24" className="h-3.5 w-3.5" fill="none" stroke="currentColor" strokeWidth={2}>
                            <path d="M12 4v12m0 0l-4-4m4 4l4-4M4 18h16" strokeLinecap="round" strokeLinejoin="round" />
                          </svg>
                          {output.name}
                        </a>
                      ))}
                    </div>
                  ) : null}
                </li>
              );
            })}
          </ul>
        )}
      </Panel>

      <p className="text-xs text-mist-500">
        Looking for the full list?{" "}
        <Link href="/dashboard/admin" className="text-signal-300 underline-offset-4 hover:underline">
          Back to administration
        </Link>
        .
      </p>
    </div>
  );
}
