import {
  adminIssueLicenseAction,
  releaseSeatAction,
  updateLicenseStatusAction,
  updateLicenseTermsAction,
} from "@/lib/actions/license-actions";
import type { LicenseSeat, LicenseWithOwner } from "@/lib/licensing";
import { FEATURE_ORDER, PLANS, PLAN_ORDER, parseFeatures } from "@/lib/licensing";
import { Badge, Button, EmptyState, Field, Panel, PanelTitle, buttonClass, inputClass } from "@/components/ui";
import { FeaturePicker } from "@/components/dashboard/feature-picker";
import { formatDateTime } from "@/lib/util";
import Link from "next/link";

const STATUSES = ["all", "active", "suspended", "revoked", "expired"] as const;

function expiryInputValue(iso: string | null): string {
  return iso ? iso.slice(0, 10) : "";
}

function statusTone(status: string, expired: boolean) {
  if (expired || status === "expired") return "warn" as const;
  if (status === "revoked") return "bad" as const;
  if (status === "suspended") return "warn" as const;
  return "good" as const;
}

function effectiveStatus(license: LicenseWithOwner): string {
  if (license.expired) return "expired";
  return license.status;
}

export function AdminLicenseManager({
  licenses,
  seatsByLicense,
  filter,
  counts,
  notice,
  error,
}: {
  licenses: LicenseWithOwner[];
  seatsByLicense: Record<string, LicenseSeat[]>;
  filter: string;
  counts: Record<string, number>;
  notice: string | null;
  error: string | null;
}) {
  return (
    <div className="space-y-6">
      <Panel>
        <PanelTitle
          title="Issue a license"
          description="Name an account to tie the key to it, or leave the email empty to mint an unassigned key that belongs to whoever claims a seat first."
        />
        <form action={adminIssueLicenseAction} className="space-y-4 p-5">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field
              label="User email"
              htmlFor="ownerEmail"
              hint="Optional. If given, that account must already exist."
            >
              <input
                id="ownerEmail"
                name="ownerEmail"
                type="email"
                className={inputClass}
                placeholder="leave empty for an unassigned key"
              />
            </Field>

            <Field label="Plan" htmlFor="plan" hint="Sets seats, quota and retention limits.">
              <select id="plan" name="plan" defaultValue="professional" className={inputClass}>
                {PLAN_ORDER.map((id) => (
                  <option key={id} value={id}>
                    {PLANS[id].name}
                  </option>
                ))}
              </select>
            </Field>
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Seats" htmlFor="seats" hint="How many users may use this license.">
              <input
                id="seats"
                name="seats"
                type="number"
                min={1}
                max={500}
                defaultValue={PLANS.professional.seats}
                className={inputClass}
              />
            </Field>

            <Field label="Expires" htmlFor="expiresAt" hint="Leave empty for no expiry.">
              <input id="expiresAt" name="expiresAt" type="date" className={inputClass} />
            </Field>
          </div>

          <Field label="Notes" htmlFor="notes" hint="Project, machine, contract…">
            <input id="notes" name="notes" className={inputClass} placeholder="Line 2 panel — contract 4471" />
          </Field>

          <FeaturePicker selected={FEATURE_ORDER} idPrefix="issue" />

          <Button type="submit" className="w-full">
            Issue license
          </Button>

          {notice ? <p className="text-xs text-good-500">{notice}</p> : null}
          {error ? <p className="text-xs text-bad-500">{error}</p> : null}
        </form>
      </Panel>

      <div className="flex flex-wrap items-center gap-2">
        {STATUSES.map((status) => {
          const active = filter === status;
          const href = status === "all" ? "/dashboard/admin" : `/dashboard/admin?licenses=${status}`;
          const label = status === "all" ? "All" : status[0].toUpperCase() + status.slice(1);
          return (
            <Link
              key={status}
              href={href}
              className={buttonClass(active ? "primary" : "ghost", "sm")}
            >
              {label}
              <span className="ml-1.5 tabular-nums opacity-70">{counts[status] ?? 0}</span>
            </Link>
          );
        })}
      </div>

      {licenses.length === 0 ? (
        <Panel>
          <EmptyState
            title="No licenses match this filter"
            description="Change the filter above, or issue the first license using the form."
          />
        </Panel>
      ) : (
        <div className="space-y-4">
          {licenses.map((license) => {
            const status = effectiveStatus(license);
            const seats = seatsByLicense[license.id] ?? [];
            const free = Math.max(0, license.activation_limit - seats.length);

            return (
              <Panel key={license.id} className="p-5">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <p className="font-mono text-xs text-mist-200">{license.key}</p>
                      <Badge tone="info">{PLANS[license.plan as keyof typeof PLANS]?.name ?? license.plan}</Badge>
                      <Badge tone={statusTone(license.status, Boolean(license.expired))}>{status}</Badge>
                    </div>
                    <p className="mt-1.5 flex flex-wrap items-center gap-1.5">
                      {(() => {
                        const granted = parseFeatures(license.features);
                        if (granted.length === 0) {
                          return <span className="text-[11px] text-bad-400">No capabilities granted</span>;
                        }
                        return (
                          <>
                            <span className="text-[11px] text-mist-500">Capabilities:</span>
                            {granted.map((feature) => (
                              <span
                                key={feature}
                                className="rounded border border-signal-400/30 bg-signal-400/10 px-1.5 py-0.5 font-mono text-[10px] text-signal-200"
                              >
                                {feature}
                              </span>
                            ))}
                          </>
                        );
                      })()}
                    </p>
                    <p className="mt-1.5 text-xs text-mist-400">
                      {license.owner_email ? (
                        <>
                          {license.owner_name} · {license.owner_email}
                        </>
                      ) : (
                        <span className="text-signal-300">
                          Unassigned — no account linked yet
                          {seats.length > 0 ? ` · ${seats.length} seat(s) claimed` : ""}
                        </span>
                      )}
                    </p>
                  </div>
                  <div className="text-right">
                    <p className="text-sm font-semibold tabular-nums text-mist-100">
                      {seats.length}/{license.activation_limit}
                    </p>
                    <p className="text-[11px] text-mist-500">seats used</p>
                  </div>
                </div>

                <dl className="mt-4 grid gap-x-6 gap-y-2 border-y border-ink-800 py-3 text-[11px] sm:grid-cols-4">
                  <div>
                    <dt className="text-mist-500">Expires</dt>
                    <dd className="mt-0.5 text-mist-300">
                      {license.expires_at ? formatDateTime(license.expires_at).slice(0, 10) : "never"}
                    </dd>
                  </div>
                  <div>
                    <dt className="text-mist-500">Last used</dt>
                    <dd className="mt-0.5 text-mist-300">
                      {license.last_used_at ? formatDateTime(license.last_used_at) : "never"}
                    </dd>
                  </div>
                  <div>
                    <dt className="text-mist-500">Issued</dt>
                    <dd className="mt-0.5 text-mist-300">{formatDateTime(license.created_at).slice(0, 10)}</dd>
                  </div>
                  <div>
                    <dt className="text-mist-500">Notes</dt>
                    <dd className="mt-0.5 truncate text-mist-300">{license.notes ?? "—"}</dd>
                  </div>
                </dl>

                <details className="group mt-4">
                  <summary className="cursor-pointer list-none text-xs text-signal-300 hover:text-signal-200">
                    Who is using this license ({seats.length} seat{seats.length === 1 ? "" : "s"}, {free} free)
                  </summary>

                  <div className="mt-3 rounded-lg border border-ink-700 bg-ink-900/50 p-3">
                    {seats.length === 0 ? (
                      <p className="text-[11px] text-mist-500">
                        No seats claimed yet. They are taken automatically as users convert with this key.
                      </p>
                    ) : (
                      <ul className="space-y-2">
                        {seats.map((seat) => (
                          <li key={seat.id} className="flex flex-wrap items-center justify-between gap-2">
                            <div className="min-w-0">
                              <p className="truncate text-xs text-mist-200">{seat.holder}</p>
                              <p className="text-[11px] text-mist-500">
                                {seat.holder_kind === "device" ? "device" : "account"} · claimed{" "}
                                {formatDateTime(seat.claimed_at).slice(0, 10)} · last used{" "}
                                {seat.last_used_at ? formatDateTime(seat.last_used_at) : "never"}
                              </p>
                            </div>
                            <form action={releaseSeatAction}>
                              <input type="hidden" name="returnTo" value="/dashboard/admin" />
                              <input type="hidden" name="id" value={license.id} />
                              <input type="hidden" name="holder" value={seat.holder} />
                              <Button type="submit" variant="outline" size="sm">
                                Release
                              </Button>
                            </form>
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                </details>

                <div className="mt-4 grid gap-4 border-t border-ink-800 pt-4 lg:grid-cols-2">
                  <form action={updateLicenseTermsAction} className="space-y-3">
                    <input type="hidden" name="returnTo" value="/dashboard/admin" />
                    <input type="hidden" name="id" value={license.id} />
                    <p className="text-[11px] font-medium uppercase tracking-[0.14em] text-mist-500">
                      Seats &amp; expiry
                    </p>
                    <div className="grid gap-3 sm:grid-cols-2">
                      <Field label="Seats" htmlFor={`seats-${license.id}`}>
                        <input
                          id={`seats-${license.id}`}
                          name="seats"
                          type="number"
                          min={1}
                          max={500}
                          defaultValue={license.activation_limit}
                          className={inputClass}
                        />
                      </Field>
                      <Field label="Expires" htmlFor={`expires-${license.id}`}>
                        <input
                          id={`expires-${license.id}`}
                          name="expiresAt"
                          type="date"
                          defaultValue={expiryInputValue(license.expires_at)}
                          className={inputClass}
                        />
                      </Field>
                    </div>
                    <FeaturePicker
                      selected={parseFeatures(license.features)}
                      idPrefix={`lic-${license.id}`}
                    />
                    <Button type="submit" variant="outline" size="sm">
                      Save terms
                    </Button>
                  </form>

                  <div className="space-y-3">
                    <p className="text-[11px] font-medium uppercase tracking-[0.14em] text-mist-500">Status</p>
                    <div className="flex flex-wrap gap-2">
                      {license.status === "active" ? (
                        <form action={updateLicenseStatusAction}>
                          <input type="hidden" name="returnTo" value="/dashboard/admin" />
                          <input type="hidden" name="id" value={license.id} />
                          <input type="hidden" name="status" value="suspended" />
                          <Button type="submit" variant="outline" size="sm">
                            Suspend
                          </Button>
                        </form>
                      ) : (
                        <form action={updateLicenseStatusAction}>
                          <input type="hidden" name="returnTo" value="/dashboard/admin" />
                          <input type="hidden" name="id" value={license.id} />
                          <input type="hidden" name="status" value="active" />
                          <Button type="submit" variant="outline" size="sm">
                            Reinstate
                          </Button>
                        </form>
                      )}
                      {license.status !== "revoked" ? (
                        <form action={updateLicenseStatusAction}>
                          <input type="hidden" name="returnTo" value="/dashboard/admin" />
                          <input type="hidden" name="id" value={license.id} />
                          <input type="hidden" name="status" value="revoked" />
                          <Button type="submit" variant="outline" size="sm">
                            Revoke
                          </Button>
                        </form>
                      ) : null}
                    </div>
                    <p className="text-[11px] leading-relaxed text-mist-500">
                      Suspending blocks new conversions immediately. Revoking is permanent for the key.
                    </p>
                  </div>
                </div>
              </Panel>
            );
          })}
        </div>
      )}
    </div>
  );
}
