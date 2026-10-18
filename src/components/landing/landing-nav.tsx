"use client";

import Link from "next/link";
import { useCallback, useState } from "react";
import { BrandLogo } from "./brand-logo";
import { useDismissable, useScrolled } from "./client";

const LINKS = [
  { href: "#pipeline", label: "Pipeline" },
  { href: "#cross-reference", label: "Cross-reference" },
  { href: "#architecture", label: "Architecture" },
  { href: "#compare", label: "Comparison" },
  { href: "#plans", label: "Pricing" },
  { href: "#about", label: "About" },
] as const;

export function LandingNav() {
  const scrolled = useScrolled(16);
  const [open, setOpen] = useState(false);
  const close = useCallback(() => setOpen(false), []);
  const trigger = useDismissable(open, close);

  return (
    <header
      className={`sticky top-0 z-50 border-b bg-paper/90 backdrop-blur-md transition-[border-color,box-shadow] duration-300 ${
        scrolled ? "border-hair shadow-[0_1px_0_0_var(--color-hair),0_14px_30px_-28px_rgba(10,12,10,0.55)]" : "border-transparent"
      }`}
    >
      <div
        className={`mx-auto flex w-full max-w-[1240px] items-center justify-between gap-6 px-5 transition-[height] duration-300 sm:px-8 ${
          scrolled ? "h-16" : "h-20"
        }`}
      >
        <Link
          href="/"
          aria-label="XFORCEMSW home"
          className="flex shrink-0 items-center outline-offset-4 focus-visible:outline-2 focus-visible:outline-brand-800"
        >
          <BrandLogo size="sm" priority />
        </Link>

        <nav aria-label="Primary" className="hidden md:block">
          <ul className="flex items-center gap-8">
            {LINKS.map((link) => (
              <li key={link.href}>
                <a
                  href={link.href}
                  className="relative py-1 text-[11px] font-semibold uppercase tracking-[0.16em] text-ink-2 transition-colors after:absolute after:inset-x-0 after:-bottom-0.5 after:h-px after:origin-left after:scale-x-0 after:bg-brand-800 after:transition-transform hover:text-ink hover:after:scale-x-100 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-brand-800"
                >
                  {link.label}
                </a>
              </li>
            ))}
          </ul>
        </nav>

        <div className="flex items-center gap-2">
          <Link
            href="/login"
            className="hidden px-3 py-2 text-[11px] font-semibold uppercase tracking-[0.16em] text-ink-2 transition-colors hover:text-ink focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-brand-800 sm:inline-flex"
          >
            Sign in
          </Link>
          <a
            href="/contact"
            className="hidden h-10 items-center border border-ink bg-ink px-5 text-[11px] font-semibold uppercase tracking-[0.16em] text-white transition-colors hover:border-brand-800 hover:bg-brand-800 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-brand-800 sm:inline-flex"
          >
            Contact us
          </a>

          <button
            ref={trigger}
            type="button"
            onClick={() => setOpen((value) => !value)}
            aria-expanded={open}
            aria-controls="landing-mobile-nav"
            className="relative -mr-2 inline-flex h-11 w-11 items-center justify-center text-ink outline-offset-4 focus-visible:outline-2 focus-visible:outline-brand-800 md:hidden"
          >
            <span className="sr-only">{open ? "Close menu" : "Open menu"}</span>
            <span aria-hidden="true" className="relative block h-4 w-6">
              <span
                className={`absolute left-0 block h-[1.5px] w-6 bg-ink transition-transform duration-300 ${
                  open ? "top-1/2 -translate-y-1/2 rotate-45" : "top-0"
                }`}
              />
              <span
                className={`absolute left-0 block h-[1.5px] w-6 bg-ink transition-transform duration-300 ${
                  open ? "top-1/2 -translate-y-1/2 -rotate-45" : "top-3.5"
                }`}
              />
            </span>
          </button>
        </div>
      </div>

      <div
        id="landing-mobile-nav"
        hidden={!open}
        className="border-t border-hair bg-paper md:hidden"
      >
        <nav aria-label="Primary mobile" className="mx-auto w-full max-w-[1240px] px-5 py-6 sm:px-8">
          <ul className="divide-y divide-hair">
            {LINKS.map((link) => (
              <li key={link.href}>
                <a
                  href={link.href}
                  onClick={close}
                  className="flex items-center justify-between py-4 text-sm font-semibold uppercase tracking-[0.14em] text-ink focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-brand-800"
                >
                  {link.label}
                  <span aria-hidden="true" className="font-mono text-[10px] text-ink-4">
                    &rarr;
                  </span>
                </a>
              </li>
            ))}
          </ul>
          <div className="mt-6 flex flex-col gap-3">
            <Link
              href="/enter-license"
              onClick={close}
              className="inline-flex h-12 items-center justify-center bg-ink text-xs font-semibold uppercase tracking-[0.16em] text-white"
            >
              Enter your license
            </Link>
            <Link
              href="/contact"
              onClick={close}
              className="inline-flex h-12 items-center justify-center border border-hair-2 text-xs font-semibold uppercase tracking-[0.16em] text-ink"
            >
              Contact us
            </Link>
          </div>
        </nav>
      </div>
    </header>
  );
}
