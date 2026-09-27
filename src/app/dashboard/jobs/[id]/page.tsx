import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getCurrentUser } from "@/lib/auth";
import { getJobForUser } from "@/lib/jobs";
import { JobStatusBadge } from "@/components/dashboard/job-bits";
import { Badge, ButtonLink, EmptyState, Panel, PanelTitle } from "@/components/ui";
import { formatBytes, formatDateTime } from "@/lib/util";
import { getDb } from "@/lib/db";

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-start justify-between gap-4 border-b border-ink-800 py-2.5 last:border-0">
      <dt className="text-xs text-mist-500">{label}</dt>
      <dd className="text-right text-xs text-mist-200">{value}</dd>
    </div>
  );
}

export default async function JobDetailPage({ params }: PageProps<"/dashboard/jobs/[id]">) {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const { id } = await params;
  const job = getJobForUser(id, user.id);
  if (!job) notFound();

  const license = job.license_id
    ? ((getDb().prepare("SELECT * FROM licenses WHERE id = ?").get(job.license_id) as
        | { key: string; plan: string }
        | undefined) ?? null)
    : null;

  const optionEntries = Object.entries(job.options).filter(
    ([, value]) => value !== null && value !== "",
  );

  return (
    <div className="space-y-8">
      <div>
        <Link
          href="/dashboard/jobs"
          className="text-xs text-mist-500 underline-offset-4 hover:text-mist-200 hover:underline"
        >
          ← All jobs
        </Link>
        <div className="mt-3 flex flex-wrap items-start justify-between gap-4">
          <div>
            <h1 className="font-mono text-lg font-semibold text-mist-100">{job.source_name}</h1>
            <p className="mt-1 font-mono text-[11px] text-ink-400">{job.id}</p>
          </div>
          <div className="flex items-center gap-2">
            <JobStatusBadge status={job.status} />
            {license ? <Badge tone="info">{license.plan} key</Badge> : null}
          </div>
        </div>
      </div>

      {job.status === "failed" && job.error ? (
        <div className="rounded-[14px] border border-bad-500/40 bg-bad-500/10 p-5">
          <p className="text-sm font-semibold text-bad-500">Conversion failed</p>
          <p className="mt-2 font-mono text-xs leading-relaxed text-mist-300">{job.error}</p>
        </div>
      ) : null}

      <div className="grid gap-6 lg:grid-cols-[1.5fr_1fr] lg:items-start">
        <div className="space-y-6">
          <Panel>
            <PanelTitle
              title="Outputs"
              description={
                job.status === "completed"
                  ? `${job.outputs.length} file${job.outputs.length === 1 ? "" : "s"} ready to download.`
                  : "Outputs appear here once the job completes."
              }
            />
            {job.outputs.length === 0 ? (
              <EmptyState
                title="No outputs yet"
                description="Either the conversion is still running, or it failed before producing a result. The error, if any, is shown above."
              />
            ) : (
              <ul className="divide-y divide-ink-800">
                {job.outputs.map((output, index) => (
                  <li key={output.path} className="flex items-center justify-between gap-4 px-5 py-3.5">
                    <div className="min-w-0">
                      <p className="truncate font-mono text-xs text-mist-200">{output.name}</p>
                      <p className="mt-0.5 text-[11px] text-mist-500">
                        {output.kind} · {output.mimeType} · {formatBytes(output.size)}
                      </p>
                    </div>
                    <a
                      href={`/api/jobs/${job.id}/output/${index}`}
                      className="shrink-0 rounded-lg border border-ink-600 px-3 py-1.5 text-xs text-mist-200 transition-colors hover:border-signal-500/60 hover:text-signal-300"
                    >
                      Download
                    </a>
                  </li>
                ))}
              </ul>
            )}
          </Panel>

          {job.warnings.length > 0 ? (
            <Panel>
              <PanelTitle title="Warnings" description="Reported by the engine. Review before shipping." />
              <ul className="space-y-2.5 px-5 py-4">
                {job.warnings.map((warning, index) => (
                  <li key={index} className="flex gap-2.5 text-xs leading-relaxed text-amberline-400">
                    <span className="mt-1.5 h-1 w-1 shrink-0 rounded-full bg-amberline-400" />
                    {warning}
                  </li>
                ))}
              </ul>
            </Panel>
          ) : null}

          {optionEntries.length > 0 ? (
            <Panel>
              <PanelTitle title="Options sent to the engine" />
              <dl className="px-5 py-3">
                {optionEntries.map(([key, value]) => (
                  <Row key={key} label={key} value={<span className="font-mono">{String(value)}</span>} />
                ))}
              </dl>
            </Panel>
          ) : null}
        </div>

        <Panel className="p-5">
          <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-mist-500">Details</p>
          <dl className="mt-4">
            <Row label="Status" value={<JobStatusBadge status={job.status} />} />
            <Row label="Engine" value={<span className="font-mono">{job.engine}</span>} />
            <Row label="Source size" value={formatBytes(job.source_size)} />
            <Row label="License" value={license ? <span className="font-mono text-[11px]">{license.key.slice(0, 14)}…</span> : "—"} />
            <Row label="Created" value={formatDateTime(job.created_at)} />
            <Row label="Started" value={formatDateTime(job.started_at)} />
            <Row label="Finished" value={formatDateTime(job.finished_at)} />
            <Row label="Progress" value={`${job.progress}%`} />
          </dl>

          <div className="mt-6">
            <ButtonLink href="/dashboard/convert" variant="outline" size="sm" className="w-full">
              Run another conversion
            </ButtonLink>
          </div>
        </Panel>
      </div>
    </div>
  );
}
