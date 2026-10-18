import { LadderPhases } from "@/components/nx/ladder-phases";
import { NxPanel, NxPanelTitle } from "@/components/nx/primitives";
import { StatusChip } from "@/components/nx/status-chip";
import {
  getEngineStatus,
  type EvidenceStatus,
  type KnownIssue,
  type ProjectStatus,
  type ReportJson,
  type StatusItem,
} from "@/lib/engine";

export const dynamic = "force-dynamic";

const GROUP_ORDER: EvidenceStatus[] = [
  "verifiedInTia",
  "unitTestOnly",
  "unverified",
  "notStarted",
  "failed",
];

function groupItems(items: StatusItem[]): Map<EvidenceStatus, StatusItem[]> {
  const groups = new Map<EvidenceStatus, StatusItem[]>();
  for (const item of items) {
    const list = groups.get(item.status);
    if (list) list.push(item);
    else groups.set(item.status, [item]);
  }
  return groups;
}

function ItemList({ items }: { items: StatusItem[] }) {
  return (
    <ul className="divide-y divide-nx-line">
      {items.map((item, index) => (
        <li key={`${item.kind}:${item.name}:${index}`} className="flex items-center gap-3 px-5 py-2.5 text-xs">
          <span className="shrink-0 border border-nx-line px-1.5 py-0.5 text-[10px] uppercase tracking-[0.06em] text-nx-hint">
            {item.kind}
          </span>
          <span className="min-w-0 flex-1 truncate font-mono text-nx-ink">{item.name}</span>
          {item.detail ? <span className="hidden text-nx-hint sm:inline">{item.detail}</span> : null}
        </li>
      ))}
    </ul>
  );
}

function Signal({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="px-5 py-4">
      <dt className="text-[11px] font-semibold uppercase tracking-[0.12em] text-nx-hint">{label}</dt>
      <dd className="mt-2 space-y-1.5">{children}</dd>
    </div>
  );
}

function KnownIssuesPanel({ report }: { report: ReportJson | null | undefined }) {
  const issues: KnownIssue[] = report?.known_issues ?? [];
  if (issues.length === 0) return null;
  return (
    <NxPanel>
      <NxPanelTitle title="Known issues" description="Open issues the engine reported. Nothing here is assumed." />
      <ul className="divide-y divide-nx-line">
        {issues.map((issue, index) => {
          const open = (issue.status ?? "open").toLowerCase() === "open";
          return (
            <li key={`${issue.id}:${index}`} className="flex gap-3 px-5 py-3 text-xs">
              <span
                aria-hidden="true"
                className={`mt-1 h-1.5 w-1.5 shrink-0 ${open ? "bg-nx-fail-fg" : "bg-nx-green-700"}`}
              />
              <div className="min-w-0">
                <p className="text-nx-ink">
                  {issue.id ? <span className="mr-2 font-mono text-nx-hint">{issue.id}</span> : null}
                  {issue.description ?? "Known issue"}
                  {typeof issue.count === "number" ? (
                    <span className="ml-2 text-nx-hint">×{issue.count}</span>
                  ) : null}
                </p>
                {issue.affected_blocks && issue.affected_blocks.length > 0 ? (
                  <p className="mt-1 truncate font-mono text-[11px] text-nx-hint">
                    {issue.affected_blocks.join(", ")}
                  </p>
                ) : null}
              </div>
            </li>
          );
        })}
      </ul>
    </NxPanel>
  );
}

function RoundTripPanel({ status }: { status: ProjectStatus }) {
  const objects = status.roundTrip?.objects ?? [];
  if (objects.length === 0) {
    return (
      <NxPanel>
        <NxPanelTitle
          title="Round trip"
          description="Importing the package in TIA with import.ps1 writes roundtrip.json."
        />
        <p className="px-5 py-4 text-xs text-nx-hint">
          No roundtrip evidence yet, so no object can reach VERIFIED IN TIA.
        </p>
      </NxPanel>
    );
  }
  return (
    <NxPanel>
      <NxPanelTitle title="Round trip" description="From roundtrip.json" />
      <div className="overflow-x-auto">
        <table className="w-full text-left text-xs">
          <thead>
            <tr className="border-b border-nx-line text-[10px] uppercase tracking-[0.06em] text-nx-hint">
              <th className="px-5 py-2 font-semibold">Kind</th>
              <th className="px-3 py-2 font-semibold">Name</th>
              <th className="px-3 py-2 font-semibold">Imported</th>
              <th className="px-3 py-2 font-semibold">Compiled</th>
              <th className="px-3 py-2 font-semibold">Messages</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-nx-line">
            {objects.map((object, index) => {
              const errors = (object.messages ?? []).filter((m) => (m.state ?? "").toLowerCase() === "error").length;
              const warnings = (object.messages ?? []).filter((m) => (m.state ?? "").toLowerCase() === "warning").length;
              return (
                <tr key={`${object.kind}:${object.name}:${index}`}>
                  <td className="px-5 py-2 text-nx-hint">{object.kind}</td>
                  <td className="px-3 py-2 font-mono text-nx-ink">{object.name}</td>
                  <td className="px-3 py-2">
                    {object.imported ? <Check /> : <Dash />}
                  </td>
                  <td className="px-3 py-2">
                    {object.compiled ? <Check /> : <Dash />}
                  </td>
                  <td className="px-3 py-2">
                    {errors > 0 ? <span className="text-nx-fail-fg">{errors} error{errors === 1 ? "" : "s"}</span> : null}
                    {errors > 0 && warnings > 0 ? <span className="text-nx-hint"> · </span> : null}
                    {warnings > 0 ? <span className="text-nx-unv-fg">{warnings} warning{warnings === 1 ? "" : "s"}</span> : null}
                    {errors === 0 && warnings === 0 ? <span className="text-nx-hint">—</span> : null}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </NxPanel>
  );
}

function Check() {
  return (
    <svg viewBox="0 0 24 24" className="h-4 w-4 text-nx-green-700" fill="none" stroke="currentColor" strokeWidth={2} aria-label="yes">
      <path d="M5 12.5 10 17.5 19 7" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

function Dash() {
  return <span className="text-nx-hint">—</span>;
}

export default async function EngineDashboardPage() {
  const { status, error } = await getEngineStatus();
  const report = status.report;
  const tests = status.tests;
  const groups = groupItems(status.items);

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-lg font-semibold text-nx-ink">Project status</h1>
        <p className="mt-1.5 max-w-2xl text-sm text-nx-body">
          Every status below is computed from evidence files on disk. Missing evidence reads as{" "}
          <span className="text-nx-ink">NOT STARTED</span>; nothing is hard-coded.
        </p>
        {status.evidencePath ? (
          <p className="mt-2 font-mono text-[11px] text-nx-hint">{status.evidencePath}</p>
        ) : null}
      </div>

      {error ? (
        <div className="border border-nx-fail-fg/40 bg-nx-fail-bg px-4 py-3 text-xs text-nx-fail-fg">
          {error}
        </div>
      ) : null}

      {status.phases.length > 0 ? (
        <NxPanel>
          <NxPanelTitle
            title="Phases"
            description="A rung lights only when its evidence reaches VERIFIED IN TIA. Counts, never percentages."
          />
          <div className="py-1">
            <LadderPhases phases={status.phases} />
          </div>
        </NxPanel>
      ) : null}

      <NxPanel>
        <NxPanelTitle title="Signals" description="Counted from evidence files." />
        <dl className="grid grid-cols-1 divide-y divide-nx-line sm:grid-cols-2 sm:divide-x lg:grid-cols-4">
          <Signal label="Build & tests">
            <StatusChip status={status.buildStatus} />
            <p className="text-xs text-nx-hint">
              {tests ? `${tests.passed}/${tests.total} tests passed` : "no tests.trx evidence"}
            </p>
          </Signal>
          <Signal label="Conversion">
            <StatusChip status={status.conversionStatus} />
            <p className="truncate font-mono text-[11px] text-nx-hint">
              {report?.source_file ?? "no report.json evidence"}
            </p>
          </Signal>
          <Signal label="Compile warnings">
            <p
              className={`text-2xl font-semibold tabular-nums ${
                status.compileWarnings > 0 ? "text-nx-unv-fg" : "text-nx-ink"
              }`}
            >
              {status.compileWarnings}
            </p>
            <p className="text-xs text-nx-hint">from roundtrip.json</p>
          </Signal>
          <Signal label="Open known issues">
            <p
              className={`text-2xl font-semibold tabular-nums ${
                status.openKnownIssues > 0 ? "text-nx-fail-fg" : "text-nx-ink"
              }`}
            >
              {status.openKnownIssues}
            </p>
            <p className="text-xs text-nx-hint">from report.json</p>
          </Signal>
        </dl>
      </NxPanel>

      {report ? (
        <NxPanel>
          <NxPanelTitle title="Conversion summary" description="From report.json" />
          <dl className="grid grid-cols-2 gap-x-8 gap-y-3 px-5 py-4 text-xs sm:grid-cols-3">
            <div>
              <dt className="text-nx-hint">Source file</dt>
              <dd className="mt-0.5 font-mono text-nx-ink">{report.source_file ?? "—"}</dd>
            </div>
            <div>
              <dt className="text-nx-hint">Source version</dt>
              <dd className="mt-0.5 text-nx-ink">{report.source_version ?? "—"}</dd>
            </div>
            <div>
              <dt className="text-nx-hint">TIA target</dt>
              <dd className="mt-0.5 text-nx-ink">{report.tia_target ?? "—"}</dd>
            </div>
            <div>
              <dt className="text-nx-hint">CPU</dt>
              <dd className="mt-0.5 font-mono text-nx-ink">{report.cpu_order_number ?? "—"}</dd>
            </div>
            <div>
              <dt className="text-nx-hint">POUs</dt>
              <dd className="mt-0.5 tabular-nums text-nx-ink">
                {report.counts?.pous_converted ?? 0}/{report.counts?.pous_total ?? 0}
              </dd>
            </div>
            <div>
              <dt className="text-nx-hint">Globals</dt>
              <dd className="mt-0.5 tabular-nums text-nx-ink">
                {report.counts?.globals_converted ?? 0}/{report.counts?.globals_total ?? 0}
              </dd>
            </div>
          </dl>
        </NxPanel>
      ) : null}

      {status.items.length > 0 ? (
        <div className="grid items-start gap-6 xl:grid-cols-2">
          {GROUP_ORDER.map((group) => {
            const items = groups.get(group);
            if (!items || items.length === 0) return null;
            return (
              <NxPanel key={group}>
                <NxPanelTitle
                  title={
                    <span className="flex items-center gap-3">
                      <StatusChip status={group} />
                      <span className="text-nx-hint">{items.length}</span>
                    </span>
                  }
                />
                <ItemList items={items} />
              </NxPanel>
            );
          })}
        </div>
      ) : null}

      <RoundTripPanel status={status} />
      <KnownIssuesPanel report={report} />
    </div>
  );
}