import Link from "next/link";
import { redirect } from "next/navigation";
import { getCurrentUser } from "@/lib/auth";
import { listJobs } from "@/lib/jobs";
import { userHasFeature } from "@/lib/licensing";
import { JobViewer } from "@/components/dashboard/job-viewer";
import { PageTitle } from "@/components/dashboard/page-title";
import { ButtonLink, EmptyState, Panel, PanelTitle } from "@/components/ui";
import { formatDateTime } from "@/lib/util";

export const dynamic = "force-dynamic";

const PT2X_RE = /\.pt2x?$/i;

function pt2xIndex(outputs: { name: string }[]): number {
  return outputs.findIndex((output) => PT2X_RE.test(output.name));
}

export default async function DashboardViewerPage({ searchParams }: PageProps<"/dashboard/viewer">) {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  if (!userHasFeature(user.id, "viewer")) {
    return (
      <div className="space-y-8">
        <PageTitle
          title="Project viewer"
          description="Open a finished PT2X conversion and browse its ladder logic sheet by sheet."
        />
        <Panel className="p-5">
          <div className="border-2 border-nx-unv-fg/50 bg-nx-unv-bg px-5 py-4 text-sm font-semibold text-nx-unv-fg">
            Your license does not include the project viewer. Contact your administrator to enable it.
          </div>
        </Panel>
      </div>
    );
  }

  const query = await searchParams;
  const jobParam = typeof query.job === "string" ? query.job : null;
  const indexParam = typeof query.index === "string" ? Number(query.index) : null;
  const jobs = listJobs(user.id, 50);
  const viewable = jobs
    .map((job) => ({ job, index: job.status === "completed" ? pt2xIndex(job.outputs) : -1 }))
    .filter((entry) => entry.index >= 0);

  // A specific job was requested: hand its stored PT2X to the viewer.
  if (jobParam) {
    const entry = viewable.find((item) => item.job.id === jobParam);
    const index = indexParam !== null && Number.isInteger(indexParam) ? indexParam : entry?.index ?? -1;
    if (entry && index >= 0) {
      return (
        <div className="space-y-6">
          <PageTitle
            title="Project viewer"
            description={`${entry.job.source_name} · finished ${entry.job.finished_at ? formatDateTime(entry.job.finished_at) : "—"}`}
            action={
              <ButtonLink href="/dashboard/viewer" variant="outline" size="sm">
                All projects
              </ButtonLink>
            }
          />
          <JobViewer jobId={entry.job.id} outputIndex={index} />
        </div>
      );
    }
  }

  return (
    <div className="space-y-8">
      <PageTitle
        title="Project viewer"
        description="Open a finished PT2X conversion and browse its ladder logic sheet by sheet."
      />

      {viewable.length === 0 ? (
        <Panel>
          <EmptyState
            title="No PT2X projects to view yet"
            description="Convert a source file to PT2X and it will appear here, ready to open in the viewer."
            action={
              <Link
                href="/dashboard/convert"
                className="inline-flex h-9 items-center rounded-lg bg-signal-500 px-4 text-sm font-semibold text-ink-950"
              >
                Start a conversion
              </Link>
            }
          />
        </Panel>
      ) : (
        <Panel>
          <PanelTitle
            title="PT2X projects"
            description={viewable.length === 1 ? "1 project" : `${viewable.length} projects`}
          />
          <ul className="divide-y divide-ink-800">
            {viewable.map(({ job, index }) => (
              <li key={job.id} className="flex flex-wrap items-center gap-4 px-5 py-3">
                <span
                  className="grid h-8 w-8 shrink-0 place-items-center rounded-md border border-signal-500/40 bg-signal-500/10 text-signal-300"
                  aria-hidden="true"
                >
                  <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth={1.5}>
                    <path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7Z" strokeLinecap="round" strokeLinejoin="round" />
                    <circle cx="12" cy="12" r="3" />
                  </svg>
                </span>
                <div className="min-w-0 flex-1">
                  <p className="truncate font-mono text-xs text-mist-200" title={job.source_name}>
                    {job.source_name}
                  </p>
                  <p className="mt-0.5 text-[11px] text-mist-500">
                    {job.outputs[index]?.name ?? "project.pt2x"} ·{" "}
                    {job.finished_at ? formatDateTime(job.finished_at) : formatDateTime(job.created_at)}
                  </p>
                </div>
                <Link
                  href={`/dashboard/viewer?job=${job.id}&index=${index}`}
                  className="shrink-0 rounded-lg border border-ink-600 px-3 py-1.5 text-xs font-medium text-mist-200 transition-colors hover:border-signal-500/60 hover:text-mist-100"
                >
                  Open
                </Link>
              </li>
            ))}
          </ul>
        </Panel>
      )}
    </div>
  );
}
