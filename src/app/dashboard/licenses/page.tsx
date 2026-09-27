import { redirect } from "next/navigation";
import { getCurrentUser } from "@/lib/auth";
import { getDashboardContext } from "@/lib/dashboard";
import { getDb } from "@/lib/db";
import { issueLicenseAction, updateLicenseStatusAction } from "@/lib/actions/license-actions";
import { PageTitle } from "@/components/dashboard/page-title";
import { Badge, Button, EmptyState, Field, Panel, PanelTitle, inputClass } from "@/components/ui";
import { PLANS, PLAN_ORDER } from "@/lib/licensing";
import { formatDate, formatDateTime, isExpired } from "@/lib/util";
import type { LicenseStatus } from "@/lib/types";

const STATUS_TONE: Record<LicenseStatus, "good" | "warn" | "bad" | "neutral"> = {
  active: "good",
  suspended: "warn",
  revoked: "bad",
  expired: "neutral",
};

const ERRORS: Record<string, string> = {
  plan_upgrade_required: "You can only issue keys for your current plan or lower.",
  license_limit: "You have reached the number of license keys included in your plan.",
  invalid_request: "That request was not valid.",
  not_found: "License not found.",
  forbidden: "That license belongs to another account.",
};

export default async function LicensesPage({ searchParams }: PageProps<"/dashboard/licenses">) {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const context = await getDashboardContext(user);
  const query = await searchParams;
  const errorKey = typeof query.error === "string" ? query.error : null;
  const issuedId = typeof query.issued === "string" ? query.issued : null;

  const issued = issuedId
    ? ((getDb().prepare("SELECT * FROM licenses WHERE id = ?").get(issuedId) as
        | { id: string; key: string }
        | undefined) ?? null)
    : null;

  const events = context.licenses[0]
    ? (getDb()
        .prepare(
          "SELECT * FROM license_events WHERE license_id = ? ORDER BY created_at DESC LIMIT 20",
        )
        .all(context.licenses[0].id) as { id: string; event: string; detail: string | null; created_at: string }[])
    : [];

  const activeCount = context.licenses.filter(
    (license) => license.status === "active" && !isExpired(license.expires_at),
  ).length;

  return (
    <div className="space-y-8">
      <PageTitle
        title="Licenses"
        description="Issue keys, control seats and expiry, and revoke access when a machine leaves the floor."
        action={
          <Badge tone="info">
            {activeCount} active · {context.plan.licenseKeys} included on {context.plan.name}
          </Badge>
        }
      />

      {errorKey && ERRORS[errorKey] ? (
        <p role="alert" className="rounded-lg border border-bad-500/40 bg-bad-500/10 px-3.5 py-2.5 text-xs text-bad-500">
          {ERRORS[errorKey]}
        </p>
      ) : null}

      {issued ? (
        <div className="rounded-[14px] border border-good-500/40 bg-good-500/10 p-5">
          <p className="text-sm font-semibold text-good-500">License issued</p>
          <p className="mt-2 break-all font-mono text-sm text-mist-100">{issued.key}</p>
          <p className="mt-2 text-xs text-mist-500">
            Copy it now — it is shown in full only here and in the list below.
          </p>
        </div>
      ) : null}

      <div className="grid gap-6 lg:grid-cols-[1.6fr_1fr] lg:items-start">
        <Panel>
          <PanelTitle
            title="Issued keys"
            description="Keys are shown once at issue time. Store them in your license server."
          />
          {context.licenses.length === 0 ? (
            <EmptyState
              title="No licenses issued"
              description="Issue a key to hand out with a deliverable, or to let the engine verify a client installation."
            />
          ) : (
            <ul className="divide-y divide-ink-800">
              {context.licenses.map((license) => {
                const expired = isExpired(license.expires_at);
                return (
                  <li key={license.id} className="px-5 py-4">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="min-w-0">
                        <p className="font-mono text-xs text-mist-200">{license.key}</p>
                        <p className="mt-1 text-[11px] text-mist-500">
                          {license.plan} · {license.activations}/{license.activation_limit} seats · issued{" "}
                          {formatDate(license.created_at)}
                          {license.expires_at ? ` · expires ${formatDate(license.expires_at)}` : " · no expiry"}
                        </p>
                        {license.notes ? (
                          <p className="mt-1 text-[11px] text-mist-500">{license.notes}</p>
                        ) : null}
                      </div>
                      <Badge tone={expired ? "neutral" : STATUS_TONE[license.status]}>
                        {expired ? "expired" : license.status}
                      </Badge>
                    </div>

                    <div className="mt-3 flex flex-wrap gap-2">
                      {license.status === "active" ? (
                        <form action={updateLicenseStatusAction}>
                          <input type="hidden" name="id" value={license.id} />
                          <input type="hidden" name="status" value="suspended" />
                          <Button type="submit" variant="subtle" size="sm">
                            Suspend
                          </Button>
                        </form>
                      ) : null}
                      {license.status !== "revoked" ? (
                        <form action={updateLicenseStatusAction}>
                          <input type="hidden" name="id" value={license.id} />
                          <input type="hidden" name="status" value="revoked" />
                          <Button type="submit" variant="danger" size="sm">
                            Revoke
                          </Button>
                        </form>
                      ) : (
                        <form action={updateLicenseStatusAction}>
                          <input type="hidden" name="id" value={license.id} />
                          <input type="hidden" name="status" value="active" />
                          <Button type="submit" variant="outline" size="sm">
                            Reinstate
                          </Button>
                        </form>
                      )}
                    </div>
                  </li>
                );
              })}
            </ul>
          )}
        </Panel>

        <div className="space-y-6">
          <Panel>
            <PanelTitle title="Issue a new key" />
            <form action={issueLicenseAction} className="space-y-4 p-5">
              <Field label="Plan" htmlFor="plan">
                <select id="plan" name="plan" defaultValue={context.plan.id} className={inputClass}>
                  {PLAN_ORDER.map((id) => (
                    <option key={id} value={id} disabled={id === "enterprise"}>
                      {PLANS[id].name} — {PLANS[id].licenseKeys} key
                      {PLANS[id].licenseKeys === 1 ? "" : "s"} included
                    </option>
                  ))}
                </select>
              </Field>

              <div className="grid gap-4 sm:grid-cols-2">
                <Field label="Seats" htmlFor="seats" hint="Max activations.">
                  <input
                    id="seats"
                    name="seats"
                    type="number"
                    min={1}
                    max={500}
                    defaultValue={PLANS[context.plan.id].seats}
                    className={inputClass}
                  />
                </Field>

                <Field label="Expires" htmlFor="expiresAt" hint="Optional.">
                  <input id="expiresAt" name="expiresAt" type="date" className={inputClass} />
                </Field>
              </div>

              <Field label="Notes" htmlFor="notes" hint="Machine name, project, customer…">
                <input id="notes" name="notes" className={inputClass} placeholder="Packaging line — PLC-02" />
              </Field>

              <Button type="submit" className="w-full">
                Issue license key
              </Button>
            </form>
          </Panel>

          <Panel>
            <PanelTitle
              title="Recent activity"
              description={context.licenses[0] ? `Newest events for ${context.licenses[0].key.slice(0, 14)}…` : undefined}
            />
            {events.length === 0 ? (
              <p className="px-5 py-6 text-xs text-mist-500">No events recorded yet.</p>
            ) : (
              <ul className="space-y-2.5 px-5 py-4">
                {events.map((event) => (
                  <li key={event.id} className="flex items-start justify-between gap-3 text-[11px]">
                    <span className="text-mist-300">
                      {event.event}
                      {event.detail ? <span className="text-mist-500"> · {event.detail}</span> : null}
                    </span>
                    <span className="shrink-0 tabular-nums text-mist-500">{formatDateTime(event.created_at)}</span>
                  </li>
                ))}
              </ul>
            )}
          </Panel>
        </div>
      </div>
    </div>
  );
}
