import Link from "next/link";
import { Badge, EmptyState, type Tone } from "@/components/ui";
import { formatBytes, formatDateTime } from "@/lib/util";
import type { DecodedJob, JobStatus } from "@/lib/types";

const STATUS_TONE: Record<JobStatus, Tone> = {
  queued: "neutral",
  processing: "info",
  completed: "good",
  failed: "bad",
};

export function JobStatusBadge({ status }: { status: JobStatus }) {
  return (
    <Badge tone={STATUS_TONE[status]}>
      {status === "processing" ? (
        <span className="h-1.5 w-1.5 rounded-full bg-signal-400" />
      ) : null}
      {status}
    </Badge>
  );
}

export function JobsTable({ jobs }: { jobs: DecodedJob[] }) {
  if (jobs.length === 0) {
    return (
      <EmptyState
        title="No conversion jobs yet"
        description="Upload a ladder diagram and the job will appear here with its status, engine version and outputs."
        action={
          <Link
            href="/dashboard/convert"
            className="inline-flex h-9 items-center rounded-lg bg-signal-500 px-4 text-sm font-semibold text-ink-950"
          >
            Upload a drawing
          </Link>
        }
      />
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[640px] text-left text-sm">
        <thead>
          <tr className="border-b border-ink-700 text-[11px] uppercase tracking-[0.12em] text-mist-500">
            <th className="px-5 py-3 font-medium">Source</th>
            <th className="px-5 py-3 font-medium">Status</th>
            <th className="px-5 py-3 font-medium">Engine</th>
            <th className="px-5 py-3 font-medium">Size</th>
            <th className="px-5 py-3 font-medium">Created</th>
            <th className="px-5 py-3" />
          </tr>
        </thead>
        <tbody>
          {jobs.map((job) => (
            <tr key={job.id} className="border-b border-ink-800 last:border-0 hover:bg-ink-850/60">
              <td className="px-5 py-3">
                <p className="truncate font-mono text-xs text-mist-200" title={job.source_name}>
                  {job.source_name}
                </p>
                <p className="mt-0.5 font-mono text-[10px] text-ink-400">{job.id}</p>
              </td>
              <td className="px-5 py-3">
                <JobStatusBadge status={job.status} />
              </td>
              <td className="px-5 py-3 font-mono text-xs text-mist-500">{job.engine}</td>
              <td className="px-5 py-3 text-xs tabular-nums text-mist-400">{formatBytes(job.source_size)}</td>
              <td className="px-5 py-3 text-xs tabular-nums text-mist-400">{formatDateTime(job.created_at)}</td>
              <td className="px-5 py-3 text-right">
                <Link
                  href={`/dashboard/jobs/${job.id}`}
                  className="text-xs text-signal-300 underline-offset-4 hover:underline"
                >
                  Open
                </Link>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
