import type { ReactNode } from "react";
import { BrandLogo } from "./brand-logo";
import { LandingFooter } from "./cta-footer";
import { LandingNav } from "./landing-nav";
import { RevealBootstrap, RevealObserver } from "./client";

/**
 * Light shell for the license and checkout pages.
 *
 * These sit between the landing page and the tool, so they deliberately share
 * the landing page's palette rather than the dark marketing shell — a visitor
 * should not appear to leave the product just to pay for it.
 */
export function FunnelShell({
  eyebrow,
  title,
  lede,
  children,
  aside,
}: {
  eyebrow: string;
  title: string;
  lede: string;
  children: ReactNode;
  aside?: ReactNode;
}) {
  return (
    <div className="surface-light flex min-h-screen flex-col bg-paper">
      <RevealBootstrap />
      <RevealObserver />

      <a
        href="#funnel"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-100 focus:h-auto focus:w-auto focus:rounded-none focus:border focus:border-ink focus:bg-paper focus:px-4 focus:py-3 focus:text-xs focus:font-semibold focus:uppercase focus:tracking-[0.14em] focus:text-ink focus:[clip:auto]"
      >
        Skip to content
      </a>

      <LandingNav />

      <main id="funnel" className="flex-1">
        <div className="relative overflow-hidden border-b border-hair">
          <div aria-hidden="true" className="grid-fine pointer-events-none absolute inset-0 opacity-50" />
          <div className="relative mx-auto w-full max-w-[1240px] px-5 pb-12 pt-14 sm:px-8 sm:pb-16 sm:pt-20">
            <p className="font-mono text-[10.5px] uppercase tracking-[0.26em] text-brand-800">{eyebrow}</p>
            <h1 className="mt-5 max-w-3xl text-balance text-[clamp(2rem,5.2vw,3.25rem)] font-semibold leading-[1.05] tracking-[-0.03em] text-ink">
              {title}
            </h1>
            <p className="mt-5 max-w-2xl text-pretty text-base leading-relaxed text-ink-2">{lede}</p>
            <div className="mt-6 flex items-center gap-3">
              <BrandLogo size="sm" priority />
            </div>
          </div>
        </div>

        <div className="mx-auto w-full max-w-[1240px] px-5 py-14 sm:px-8 sm:py-20">
          <div className={aside ? "grid gap-10 lg:grid-cols-[1.15fr_1fr] lg:gap-16" : ""}>
            {children}
            {aside}
          </div>
        </div>
      </main>

      <LandingFooter />
    </div>
  );
}

/** Labelled field styled for the light surface. */
export function LightField({
  label,
  htmlFor,
  hint,
  children,
}: {
  label: string;
  htmlFor: string;
  hint?: string;
  children: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-2">
      <label htmlFor={htmlFor} className="text-[11px] font-semibold uppercase tracking-[0.16em] text-ink-3">
        {label}
      </label>
      {children}
      {hint ? <p className="text-[11px] leading-relaxed text-ink-4">{hint}</p> : null}
    </div>
  );
}

export const lightInputClass =
  "h-12 w-full border border-hair-2 bg-paper px-4 text-sm text-ink outline-offset-2 transition-colors placeholder:text-ink-4 focus:border-ink focus-visible:outline-2 focus-visible:outline-brand-800";

/** Inline status strip for success and error feedback. */
export function Notice({ tone, children }: { tone: "good" | "bad" | "info"; children: ReactNode }) {
  const tones = {
    good: "border-brand-800/30 bg-brand-050 text-brand-900",
    bad: "border-red-300 bg-red-50 text-red-800",
    info: "border-hair-2 bg-paper-2 text-ink-2",
  } as const;
  return (
    <div role={tone === "bad" ? "alert" : "status"} className={`border px-4 py-3 text-sm leading-relaxed ${tones[tone]}`}>
      {children}
    </div>
  );
}
