import { redirect } from "next/navigation";
import { getCurrentUser } from "@/lib/auth";
import { getDb } from "@/lib/db";
import { PageTitle } from "@/components/dashboard/page-title";
import { Badge, Panel, PanelTitle, Stat } from "@/components/ui";
import { formatBytes, formatDateTime } from "@/lib/util";
import { databasePath, storageDir } from "@/lib/env";
import { engineStatus } from "@/lib/converter";

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

type LicenseRow = {
  id: string;
  key: string;
  plan: string;
  status: string;
  activations: number;
  activation_limit: number;
  expires_at: string | null;
  email: string;
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

export default async function AdminPage() {
  const user = await getCurrentUser();
  if (!user) redirect("/login");
  if (user.role !== "admin") redirect("/dashboard");

  const db = getDb();
  const engine = await engineStatus();

  const users = db
    .prepare(
      `SELECT users.id, users.email, users.name, users.company, users.role, users.plan, users.created_at,
              (SELECT COUNT(*) FROM jobs WHERE jobs.user_id = users.id) AS jobs
       FROM users ORDER BY users.created_at DESC LIMIT 100`,
    )
    .all() as UserRow[];

  const licenses = db
    .prepare(
      `SELECT licenses.id, licenses.key, licenses.plan, licenses.status, licenses.activations,
              licenses.activation_limit, licenses.expires_at, users.email
       FROM licenses JOIN users ON users.id = licenses.user_id
       ORDER BY licenses.created_at DESC LIMIT 100`,
    )
    .all() as LicenseRow[];

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

  return (
    <div className="space-y-8">
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
        <Panel>
          <PanelTitle title="Users" description="Most recent 100 accounts." />
          <div className="overflow-x-auto">
            <table className="w-full min-w-[520px] text-left text-xs">
              <thead>
                <tr className="border-b border-ink-700 text-[11px] uppercase tracking-[0.12em] text-mist-500">
                  <th className="px-5 py-3 font-medium">Account</th>
                  <th className="px-5 py-3 font-medium">Plan</th>
                  <th className="px-5 py-3 font-medium">Jobs</th>
                  <th className="px-5 py-3 font-medium">Joined</th>
                </tr>
              </thead>
              <tbody>
                {users.map((row) => (
                  <tr key={row.id} className="border-b border-ink-800 last:border-0">
                    <td className="px-5 py-2.5">
                      <p className="text-mist-200">{row.name}</p>
                      <p className="text-[11px] text-mist-500">{row.email}</p>
                    </td>
                    <td className="px-5 py-2.5">
                      <Badge tone={row.role === "admin" ? "bad" : "neutral"}>
                        {row.role === "admin" ? "admin" : row.plan}
                      </Badge>
                    </td>
                    <td className="px-5 py-2.5 tabular-nums text-mist-400">{row.jobs}</td>
                    <td className="px-5 py-2.5 tabular-nums text-mist-500">
                      {formatDateTime(row.created_at).slice(0, 10)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>

        <Panel>
          <PanelTitle title="Licenses" description="Most recent 100 keys across all accounts." />
          <div className="overflow-x-auto">
            <table className="w-full min-w-[520px] text-left text-xs">
              <thead>
                <tr className="border-b border-ink-700 text-[11px] uppercase tracking-[0.12em] text-mist-500">
                  <th className="px-5 py-3 font-medium">Key</th>
                  <th className="px-5 py-3 font-medium">Owner</th>
                  <th className="px-5 py-3 font-medium">Seats</th>
                  <th className="px-5 py-3 font-medium">Status</th>
                </tr>
              </thead>
              <tbody>
                {licenses.map((row) => (
                  <tr key={row.id} className="border-b border-ink-800 last:border-0">
                    <td className="px-5 py-2.5 font-mono text-[11px] text-mist-300">
                      {row.key.slice(0, 20)}…
                    </td>
                    <td className="px-5 py-2.5 text-mist-400">{row.email}</td>
                    <td className="px-5 py-2.5 tabular-nums text-mist-400">
                      {row.activations}/{row.activation_limit}
                    </td>
                    <td className="px-5 py-2.5">
                      <Badge
                        tone={
                          row.status === "active"
                            ? "good"
                            : row.status === "revoked"
                              ? "bad"
                              : "warn"
                        }
                      >
                        {row.status}
                      </Badge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>

        <Panel>
          <PanelTitle title="Inbound messages" description="Contact form and plan change requests." />
          {messages.length === 0 ? (
            <p className="px-5 py-6 text-xs text-mist-500">No messages yet.</p>
          ) : (
            <ul className="divide-y divide-ink-800">
              {messages.map((message) => (
                <li key={message.id} className="px-5 py-3.5">
                  <p className="text-xs text-mist-200">{message.subject ?? "No subject"}</p>
                  <p className="mt-0.5 text-[11px] text-mist-500">
                    {message.name} · {message.email} · {formatDateTime(message.created_at)}
                  </p>
                  <p className="mt-1.5 line-clamp-3 text-[11px] leading-relaxed text-mist-400">
                    {message.body}
                  </p>
                </li>
              ))}
            </ul>
          )}
        </Panel>

        <Panel>
          <PanelTitle title="Audit log" description="Newest 25 recorded events." />
          <ul className="divide-y divide-ink-800">
            {audit.map((entry) => (
              <li key={entry.id} className="flex items-start justify-between gap-3 px-5 py-2.5 text-[11px]">
                <span className="font-mono text-mist-300">{entry.action}</span>
                <span className="shrink-0 tabular-nums text-mist-500">
                  {formatDateTime(entry.created_at)}
                </span>
              </li>
            ))}
          </ul>
        </Panel>
      </div>

      <Panel className="p-5">
        <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-mist-500">Runtime</p>
        <dl className="mt-4 grid gap-4 text-xs sm:grid-cols-3">
          {[
            ["Database", databasePath()],
            ["Storage", storageDir()],
            ["Engine", engine.detail],
          ].map(([label, value]) => (
            <div key={label}>
              <dt className="text-mist-500">{label}</dt>
              <dd className="mt-1 break-all font-mono text-[11px] text-mist-300">{value}</dd>
            </div>
          ))}
        </dl>
      </Panel>
    </div>
  );
}
