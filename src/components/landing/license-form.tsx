import { redeemLicenseKeyAction } from "@/lib/actions/access-actions";
import { ActionButton } from "./primitives";
import { LightField, lightInputClass, Notice } from "./funnel-shell";

/**
 * License-first entry.
 *
 * A plain form posting to a server action — no client state, so it works
 * before hydration and cannot get stuck. Two fields: the key (the credential)
 * and an email (which names the account the seat is recorded against).
 */
export function LicenseEntryForm({
  error,
  defaultKey = "",
  defaultEmail = "",
  busy = false,
}: {
  error?: string | null;
  defaultKey?: string;
  defaultEmail?: string;
  busy?: boolean;
}) {
  return (
    <form action={redeemLicenseKeyAction} className="space-y-5">
      {error ? <Notice tone="bad">{error}</Notice> : null}

      <LightField
        label="License key"
        htmlFor="key"
        hint="The key you were sent when you bought or were issued a license."
      >
        <input
          id="key"
          name="key"
          required
          autoComplete="off"
          spellCheck={false}
          defaultValue={defaultKey}
          placeholder="XFMS-XXXX-XXXX-XXXX"
          className={`${lightInputClass} font-mono uppercase tracking-[0.12em]`}
        />
      </LightField>

      <LightField
        label="Email"
        htmlFor="email"
        hint="Used to name your account and hold the seat. It is not a password and no confirmation is sent."
      >
        <input
          id="email"
          name="email"
          type="email"
          required
          autoComplete="email"
          defaultValue={defaultEmail}
          placeholder="you@company.com"
          className={lightInputClass}
        />
      </LightField>

      <div className="flex flex-wrap items-center gap-4 pt-1">
        <ActionButton type="submit" size="lg" disabled={busy} className="w-full sm:w-auto">
          {busy ? "Checking…" : "Continue with this license"}
        </ActionButton>
        <a
          href="/pricing"
          className="text-[11px] font-semibold uppercase tracking-[0.14em] text-ink-3 underline-offset-4 transition-colors hover:text-ink hover:underline focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-brand-800"
        >
          I need a license
        </a>
      </div>
    </form>
  );
}
