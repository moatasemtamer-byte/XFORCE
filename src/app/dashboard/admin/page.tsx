import { redirect } from "next/navigation";
import { getCurrentUser } from "@/lib/auth";
import { getDb } from "@/lib/db";
import { PageTitle } from "@/components/dashboard/page-title";
import { AdminLicenseManager } from "@/components/dashboard/admin-licenses";
import { KeyRevealModal } from "@/components/dashboard/key-reveal-modal";
import { Badge, Panel, PanelTitle, Stat } from "@/components/ui";
import { formatBytes, formatDateTime } from "@/lib/util";
import { databasePath, storageDir } from "@/lib/env";
import { engineStatus } from "@/lib/converter";
import { listLicensesForAdmin, listSeats, PLANS, type LicenseSeat } from "@/lib/licensing";

type UserRow = {
  id: string;
  email: string;
  name: string;
  company: string | null;
  role: string;
  plan: string;
  created_at: string;
  jobs: number;
};

type MessageRow = {
  id: string;
  name: string;
  email: string;
  subject: string | null;
  body: string;
  created_at: string;
};

type AuditRow = {
  id: string;
  action: string;
  detail: string | null;
  created_at: string;
};

export default async function AdminPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const user = await getCurrentUser();
  if (!user) redirect("/login");
  if (user.role !== "admin") redirect("/dashboard");

  const params = await searchParams;
  const filterRaw = typeof params.licenses === "string" ? params.licenses : "all";
  const filter = ["all", "active", "suspended", "revoked", "expired"].includes(filterRaw) ? filterRaw : "all";

  const db = getDb();
  const engine = await engineStatus();

  const users = db
    .prepare(
      `SELECT users.id, users.email, users.name, users.company, users.role, users.plan, users.created_at,
              (SELECT COUNT(*) FROM jobs WHERE jobs.user_id = users.id) AS jobs
       FROM users ORDER BY users.created_at DESC LIMIT 100`,
    )
    .all() as UserRow[];

  const allLicenses = listLicensesForAdmin(200);
  const counts = { all: allLicenses.length } as Record<string, number>;
  for (const license of allLicenses) {
    const status = license.expired ? "expired" : license.status;
    counts[status] = (counts[status] ?? 0) + 1;
  }
  const licenses = allLicenses.filter(
    (license) => filter === "all" || (license.expired ? "expired" : license.status) === filter,
  );
  const seatsByLicense: Record<string, LicenseSeat[]> = {};
  for (const license of licenses) seatsByLicense[license.id] = listSeats(license.id);

  const messages = db
    .prepare("SELECT * FROM messages ORDER BY created_at DESC LIMIT 25")
    .all() as MessageRow[];

  const audit = db
    .prepare("SELECT * FROM audit_log ORDER BY created_at DESC LIMIT 25")
    .all() as AuditRow[];

  const stats = db
    .prepare(
      `SELECT (SELECT COUNT(*) FROM users) AS users,
              (SELECT COUNT(*) FROM licenses) AS licenses,
              (SELECT COUNT(*) FROM jobs) AS jobs,
              (SELECT COALESCE(SUM(source_size), 0) FROM jobs) AS bytes`,
    )
    .get() as { users: number; licenses: number; jobs: number; bytes: number };

  const notice = params.issued
    ? "License issued. Copy the key from the list below and send it to the user."
    : params.updated
      ? "License terms updated."
      : params.released
        ? "Seat released and now available again."
        : null;

  const issuedId = typeof params.issued === "string" ? params.issued : null;
  const issuedLicense = issuedId ? allLicenses.find((license) => license.id === issuedId) ?? null : null;
  const error = params.error
    ? {
        no_such_user: "No account with that email. Ask the user to register first.",
        forbidden: "You do not have permission to do that.",
        not_found: "That license no longer exists.",
        invalid_request: "Check the values and try again.",
      }[params.error as string] ?? "Something went wrong."
    : null;

  return (
    <div className="space-y-8">
      {issuedLicense ? (
        <KeyRevealModal
          keyValue={issuedLicense.key}
          plan={PLANS[issuedLicense.plan as keyof typeof PLANS]?.name ?? issuedLicense.plan}
          ownerLabel={issuedLicense.owner_email ?? null}
          closeHref="/dashboard/admin"
        />
      ) : null}
      <PageTitle
        title="Administration"
        description="Accounts, licenses, inbound messages and the audit trail."
        action={<Badge tone="bad">admin only</Badge>}
      />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Stat label="Users" value={stats.users} />
        <Stat label="Licenses" value={stats.licenses} tone="info" />
        <Stat label="Jobs" value={stats.jobs} />
        <Stat label="Uploaded volume" value={formatBytes(stats.bytes)} />
      </div>

      <div className="grid gap-6 xl:grid-cols-2">
        <Panel className="xl:col-span-2">
          <PanelTitle
            title="Users"
            description="Most recent 100 accounts."
          />
          <div className="overflow-x-auto">
            <table className="w-full min-w-[520px] text-left text-xs">
              <thead>
                <tr className="border-b border-nx-line text-[11px] uppercase tracking-[0.12em] text-nx-hint">
                  <th className="px-5 py-3 font-medium">Account</th>
                  <th className="px-5 py-3 font-medium">Plan</th>
                  <th className="px-5 py-3 font-medium">Jobs</th>
                  <th className="px-5 py-3 font-medium">Joined</th>
                </tr>
              </thead>
              <tbody>
                {users.map((row) => (
                  <tr key={row.id} className="border-b border-nx-line last:border-0">
                    <td className="px-5 py-2.5">
                      <p className="text-nx-ink">{row.name}</p>
                      <p className="text-[11px] text-nx-hint">{row.email}</p>
                    </td>
                    <td className="px-5 py-2.5">
                      <Badge tone={row.role === "admin" ? "bad" : "neutral"}>
                        {row.role === "admin" ? "admin" : row.plan}
                      </Badge>
                    </td>
                    <td className="px-5 py-2.5 tabular-nums text-nx-body">{row.jobs}</td>
                    <td className="px-5 py-2.5 tabular-nums text-nx-hint">
                      {formatDateTime(row.created_at).slice(0, 10)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>

        <div className="xl:col-span-2">
          <AdminLicenseManager
            licenses={licenses}
            seatsByLicense={seatsByLicense}
            filter={filter}
            counts={counts}
            notice={notice}
            error={error}
          />
        </div>

        <Panel>
          <PanelTitle title="Inbound messages" description="Contact form and plan change requests." />
          {messages.length === 0 ? (
            <p className="px-5 py-6 text-xs text-nx-hint">No messages yet.</p>
          ) : (
            <ul className="divide-y divide-nx-line">
              {messages.map((message) => (
                <li key={message.id} className="px-5 py-3.5">
                  <p className="text-xs text-nx-ink">{message.subject ?? "No subject"}</p>
                  <p className="mt-0.5 text-[11px] text-nx-hint">
                    {message.name} · {message.email} · {formatDateTime(message.created_at)}
                  </p>
                  <p className="mt-1.5 line-clamp-3 text-[11px] leading-relaxed text-nx-body">
                    {message.body}
                  </p>
                </li>
              ))}
            </ul>
          )}
        </Panel>

        <Panel>
          <PanelTitle title="Audit log" description="Newest 25 recorded events." />
          <ul className="divide-y divide-nx-line">
            {audit.map((entry) => (
              <li key={entry.id} className="flex items-start justify-between gap-3 px-5 py-2.5 text-[11px]">
                <span className="font-mono text-nx-body">{entry.action}</span>
                <span className="shrink-0 tabular-nums text-nx-hint">
                  {formatDateTime(entry.created_at)}
                </span>
              </li>
            ))}
          </ul>
        </Panel>
      </div>

      <Panel className="p-5">
        <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-nx-hint">Runtime</p>
        <dl className="mt-4 grid gap-4 text-xs sm:grid-cols-3">
          {[
            ["Database", databasePath()],
            ["Storage", storageDir()],
            ["Engine", engine.detail],
          ].map(([label, value]) => (
            <div key={label}>
              <dt className="text-nx-hint">{label}</dt>
              <dd className="mt-1 break-all font-mono text-[11px] text-nx-body">{value}</dd>
            </div>
          ))}
        </dl>
      </Panel>
    </div>
  );
}
