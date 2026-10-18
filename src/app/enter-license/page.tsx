import type { Metadata } from "next";
import { getCurrentUser } from "@/lib/auth";
import { accessErrorMessage } from "@/lib/access-messages";
import { getDb } from "@/lib/db";
import { FunnelShell, Notice } from "@/components/landing/funnel-shell";
import { LicenseEntryForm } from "@/components/landing/license-form";
import { ActionLink, MicroLabel } from "@/components/landing/primitives";
import { PLANS } from "@/lib/licensing";
import type { PlanId } from "@/lib/types";

export const metadata: Metadata = {
  title: "Enter your license",
  description:
    "Paste your XFORCEMSW license key to open the conversion pipeline. No account form — the key is your credential.",
};

type LicenseRow = { id: string; key: string; plan: PlanId; seats: number; status: string; expires_at: string | null };

const STEPS = [
  { n: "01", title: "Paste the key", body: "Use the key from your purchase email or from the person who issued it." },
  { n: "02", title: "Name the seat", body: "Your email identifies the account holding the seat. Seats are first-come, first-served." },
  { n: "03", title: "Start converting", body: "The key is saved to your account, so you only enter it once on this device." },
] as const;

export default async function EnterLicensePage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const params = await searchParams;
  const error = accessErrorMessage(typeof params.error === "string" ? params.error : undefined);
  const issuedId = typeof params.issued === "string" ? params.issued : null;
  const issuedPlan = typeof params.plan === "string" && params.plan in PLANS ? (params.plan as PlanId) : null;

  const user = await getCurrentUser();

  // Only reveal a freshly purchased key to the signed-in owner of that license.
  const issued: LicenseRow | null =
    issuedId && user
      ? ((getDb()
          .prepare("SELECT id, key, plan, seats, status, expires_at FROM licenses WHERE id = ? AND user_id = ?")
          .get(issuedId, user.id) as LicenseRow | undefined) ?? null)
      : null;

  return (
    <FunnelShell
      eyebrow="Access"
      title="Enter your license"
      lede="Paste the key you were given and you are in. There is no account form to fill in — the key is the credential, and your email only names the seat."
      aside={
        <div className="space-y-8">
          <div>
            <MicroLabel>How it works</MicroLabel>
            <ol className="mt-5 space-y-5">
              {STEPS.map((step) => (
                <li key={step.n} className="border-t border-hair pt-4">
                  <span className="font-mono text-[10.5px] uppercase tracking-[0.2em] text-brand-800">{step.n}</span>
                  <h2 className="mt-1.5 text-sm font-semibold text-ink">{step.title}</h2>
                  <p className="mt-1.5 text-[13px] leading-relaxed text-ink-2">{step.body}</p>
                </li>
              ))}
            </ol>
          </div>

          <div className="border border-hair bg-paper-2 p-6">
            <MicroLabel>No key yet</MicroLabel>
            <p className="mt-3 text-sm leading-relaxed text-ink-2">
              Pick a plan and the license is generated for you straight away.
            </p>
            <div className="mt-5 flex flex-wrap gap-3">
              <ActionLink href="/pricing" size="md">
                See pricing
              </ActionLink>
              <ActionLink href="/contact" tone="outline" size="md">
                Contact us
              </ActionLink>
            </div>
          </div>
        </div>
      }
    >
      <div className="max-w-xl">
        {issued ? (
          <div className="space-y-6">
            <Notice tone="good">
              Your {issuedPlan ? PLANS[issuedPlan].name : "license"} is ready. Copy the key below and keep it — this is
              the only copy shown here.
            </Notice>

            <div className="border border-ink bg-ink p-6">
              <MicroLabel tone="ink">License key</MicroLabel>
              <p className="mt-3 select-all break-all font-mono text-lg tracking-[0.14em] text-brand-500">{issued.key}</p>
              <dl className="mt-5 grid grid-cols-2 gap-4 border-t border-white/10 pt-4 text-[11px]">
                <div>
                  <dt className="text-mist-400">Seats</dt>
                  <dd className="mt-1 font-mono text-mist-200">{issued.seats}</dd>
                </div>
                <div>
                  <dt className="text-mist-400">Status</dt>
                  <dd className="mt-1 font-mono text-mist-200">{issued.status}</dd>
                </div>
              </dl>
            </div>

            <div className="flex flex-wrap gap-3">
              <ActionLink href="/enter-license" size="md">
                I have another key
              </ActionLink>
              <ActionLink href="/dashboard" tone="outline" size="md">
                Go to the dashboard
              </ActionLink>
            </div>
          </div>
        ) : (
          <LicenseEntryForm error={error} />
        )}
      </div>
    </FunnelShell>
  );
}
