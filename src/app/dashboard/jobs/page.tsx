import Link from "next/link";
import { redirect } from "next/navigation";
import { getCurrentUser } from "@/lib/auth";
import { listJobs } from "@/lib/jobs";
import { PageTitle } from "@/components/dashboard/page-title";
import { JobsTable } from "@/components/dashboard/job-bits";
import { ButtonLink, Panel, cx } from "@/components/ui";
import type { JobStatus } from "@/lib/types";

const FILTERS: { key: string; label: string; value: JobStatus | "all" }[] = [
  { key: "all", label: "All", value: "all" },
  { key: "completed", label: "Completed", value: "completed" },
  { key: "failed", label: "Failed", value: "failed" },
  { key: "processing", label: "In progress", value: "processing" },
];

export default async function JobsPage({ searchParams }: PageProps<"/dashboard/jobs">) {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const query = await searchParams;
  const active = typeof query.status === "string" ? query.status : "all";
  const all = listJobs(user.id, 200);
  const jobs = active === "all" ? all : all.filter((job) => job.status === active);

  return (
    <div className="space-y-8">
      <PageTitle
        title="Job history"
        description="Every conversion attempt on this account, newest first, with engine version and outputs."
        action={
          <ButtonLink href="/dashboard/convert" size="sm">
            New conversion
          </ButtonLink>
        }
      />

      <div className="flex flex-wrap gap-2">
        {FILTERS.map((filter) => (
          <Link
            key={filter.key}
            href={filter.key === "all" ? "/dashboard/jobs" : `/dashboard/jobs?status=${filter.key}`}
            className={cx(
              "rounded-full border px-3.5 py-1.5 text-xs transition-colors",
              active === filter.key
                ? "border-signal-500/50 bg-signal-500/10 text-signal-300"
                : "border-ink-700 text-mist-400 hover:border-ink-500 hover:text-mist-200",
            )}
          >
            {filter.label}
            <span className="ml-1.5 text-mist-500">
              {filter.value === "all" ? all.length : all.filter((job) => job.status === filter.value).length}
            </span>
          </Link>
        ))}
      </div>

      <Panel>
        <JobsTable jobs={jobs} />
      </Panel>
    </div>
  );
}
