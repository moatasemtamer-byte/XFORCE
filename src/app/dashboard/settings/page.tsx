import { redirect } from "next/navigation";
import { getCurrentUser } from "@/lib/auth";
import { getDashboardContext } from "@/lib/dashboard";
import { changePasswordAction, updateProfileAction } from "@/lib/actions/account-actions";
import { PageTitle } from "@/components/dashboard/page-title";
import { Badge, Button, Field, Panel, PanelTitle, inputClass } from "@/components/ui";
import { formatDateTime } from "@/lib/util";

const ERRORS: Record<string, string> = {
  invalid_profile: "Enter a name and a valid email address.",
  email_taken: "That email address is already in use.",
  wrong_password: "Your current password is not correct.",
  weak_password: "The new password must be 10+ characters with a letter and a number.",
};

export default async function SettingsPage({ searchParams }: PageProps<"/dashboard/settings">) {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const context = await getDashboardContext(user);
  const query = await searchParams;
  const error = typeof query.error === "string" ? query.error : null;
  const saved = typeof query.saved === "string" ? query.saved : null;

  const sessions = (
    await import("@/lib/db")
  ).getDb()
    .prepare(
      "SELECT id, created_at, expires_at, user_agent FROM sessions WHERE user_id = ? ORDER BY created_at DESC LIMIT 5",
    )
    .all(user.id) as { id: string; created_at: string; expires_at: string; user_agent: string | null }[];

  return (
    <div className="space-y-8">
      <PageTitle title="Settings" description="Profile, password and active sessions on this account." />

      {error && ERRORS[error] ? (
        <p role="alert" className="rounded-lg border border-bad-500/40 bg-bad-500/10 px-3.5 py-2.5 text-xs text-bad-500">
          {ERRORS[error]}
        </p>
      ) : null}
      {saved ? (
        <p role="status" className="rounded-lg border border-good-500/40 bg-good-500/10 px-3.5 py-2.5 text-xs text-good-500">
          {saved === "password" ? "Password updated." : "Profile updated."}
        </p>
      ) : null}

      <div className="grid gap-6 lg:grid-cols-2 lg:items-start">
        <Panel>
          <PanelTitle title="Profile" description="Used on job records and license notes." />
          <form action={updateProfileAction} className="space-y-4 p-5">
            <Field label="Full name" htmlFor="name">
              <input id="name" name="name" defaultValue={user.name} required className={inputClass} />
            </Field>
            <Field label="Email" htmlFor="email">
              <input
                id="email"
                name="email"
                type="email"
                defaultValue={user.email}
                required
                className={inputClass}
              />
            </Field>
            <Field label="Company" htmlFor="company" hint="Optional.">
              <input
                id="company"
                name="company"
                defaultValue={user.company ?? ""}
                className={inputClass}
                placeholder="Your company"
              />
            </Field>
            <Button type="submit">Save profile</Button>
          </form>
        </Panel>

        <div className="space-y-6">
          <Panel>
            <PanelTitle title="Change password" description="Minimum 10 characters, with a letter and a number." />
            <form action={changePasswordAction} className="space-y-4 p-5">
              <Field label="Current password" htmlFor="currentPassword">
                <input
                  id="currentPassword"
                  name="currentPassword"
                  type="password"
                  autoComplete="current-password"
                  required
                  className={inputClass}
                />
              </Field>
              <Field label="New password" htmlFor="newPassword">
                <input
                  id="newPassword"
                  name="newPassword"
                  type="password"
                  autoComplete="new-password"
                  required
                  className={inputClass}
                />
              </Field>
              <Button type="submit" variant="subtle">
                Update password
              </Button>
            </form>
          </Panel>

          <Panel>
            <PanelTitle
              title="Active sessions"
              description="Devices currently signed in to this account."
            />
            {sessions.length === 0 ? (
              <p className="px-5 py-6 text-xs text-mist-500">No active sessions.</p>
            ) : (
              <ul className="divide-y divide-ink-800">
                {sessions.map((session) => (
                  <li key={session.id} className="px-5 py-3">
                    <p className="truncate text-xs text-mist-300">
                      {session.user_agent ?? "Unknown device"}
                    </p>
                    <p className="mt-0.5 text-[11px] text-mist-500">
                      Started {formatDateTime(session.created_at)} · expires{" "}
                      {formatDateTime(session.expires_at)}
                    </p>
                  </li>
                ))}
              </ul>
            )}
          </Panel>
        </div>
      </div>

      <Panel className="p-5">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <p className="text-sm font-semibold text-mist-100">Plan and entitlements</p>
            <p className="mt-1.5 text-xs leading-relaxed text-mist-500">
              {context.plan.name} · {context.usedThisMonth} of{" "}
              {Number.isFinite(context.plan.monthlyConversions)
                ? context.plan.monthlyConversions
                : "unlimited"}{" "}
              conversions used this month · {context.plan.retentionDays}-day retention.
            </p>
          </div>
          <div className="flex items-center gap-2">
            <Badge tone={context.engine.ready ? "good" : "warn"}>
              Engine {context.engine.ready ? "online" : "pending"}
            </Badge>
            <Badge tone="info">{context.plan.name}</Badge>
          </div>
        </div>
      </Panel>
    </div>
  );
}
