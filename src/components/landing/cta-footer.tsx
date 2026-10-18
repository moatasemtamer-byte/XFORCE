import Link from "next/link";
import { BrandLogo } from "./brand-logo";
import { ActionLink, Lede, Reveal, Section, SectionTitle } from "./primitives";

export function CtaSection() {
  return (
    <Section id="start" index="15" label="Get started" tone="ink">
      <div className="relative overflow-hidden pb-20 pt-14 sm:pb-28 sm:pt-20">
        <div aria-hidden="true" className="grid-coarse pointer-events-none absolute inset-0 opacity-[0.07]" />
        <div
          aria-hidden="true"
          className="bloom pointer-events-none absolute left-1/2 top-0 h-[120%] w-[80%] -translate-x-1/2 -translate-y-1/2 opacity-40"
        />

        <Reveal className="relative mx-auto max-w-3xl text-center">
            <SectionTitle id="start-title" tone="ink" className="text-balance">
              Industrial logic, reconstructed.
            </SectionTitle>
            <Lede tone="ink" className="mx-auto mt-6 max-w-xl text-base sm:text-lg">
              Already have a key? Enter it and start analysing. Not yet? Pick a plan and a license is generated for you
              straight away.
            </Lede>
            <div className="mt-10 flex flex-wrap items-center justify-center gap-3">
              <ActionLink href="/enter-license" size="lg" className="border-white bg-white !text-ink hover:!border-brand-500 hover:!bg-brand-500 hover:!text-ink">
                Enter your license
              </ActionLink>
              <ActionLink href="/pricing" tone="onInk" size="lg">
                Get a license
              </ActionLink>
              <ActionLink href="/contact" tone="onInk" size="lg">
                Contact us
              </ActionLink>
            </div>
        </Reveal>
      </div>
    </Section>
  );
}

const COLUMNS = [
  {
    heading: "Product",
    links: [
      { href: "#problem", label: "The problem" },
      { href: "#pipeline", label: "Analysis pipeline" },
      { href: "#structure", label: "Structure, not words" },
      { href: "#cross-reference", label: "Cross-reference" },
      { href: "#review", label: "Engineering review" },
      { href: "#architecture", label: "Architecture" },
    ],
  },
  {
    heading: "More",
    links: [
      { href: "#compare", label: "Comparison" },
      { href: "#deterministic", label: "Deterministic · offline" },
      { href: "#artefacts", label: "Artefacts" },
      { href: "#boundaries", label: "Boundaries" },
      { href: "#modernization", label: "Modernization" },
      { href: "#about", label: "About" },
    ],
  },
  {
    heading: "Licenses",
    links: [
      { href: "/enter-license", label: "Enter your license" },
      { href: "#plans", label: "Pricing" },
      { href: "/checkout?plan=professional", label: "Get a license" },
      { href: "/contact", label: "Contact us" },
      { href: "/login", label: "Sign in" },
    ],
  },
] as const;

export function LandingFooter() {
  return (
    <footer className="border-t border-hair bg-paper">
      <div className="mx-auto w-full max-w-[1240px] px-5 py-14 sm:px-8 sm:py-20">
        <div className="grid gap-12 lg:grid-cols-[1.4fr_2fr]">
          <div>
            <Link
              href="/"
              aria-label="XFORCEMSW home"
              className="inline-block outline-offset-4 focus-visible:outline-2 focus-visible:outline-brand-800"
            >
              <BrandLogo size="sm" />
            </Link>
            <p className="mt-6 max-w-xs font-mono text-[11px] uppercase leading-relaxed tracking-[0.18em] text-ink-3">
              Industrial logic
              <br />
              reconstructed
            </p>
            <p className="mt-6 max-w-sm text-sm leading-relaxed text-ink-2">
              Engineering software that reads industrial control logic and turns it into structured, analysable
              engineering data.
            </p>
          </div>

          <nav aria-label="Footer" className="grid gap-10 sm:grid-cols-3">
            {COLUMNS.map((column) => (
              <div key={column.heading}>
                <h2 className="font-mono text-[10px] uppercase tracking-[0.2em] text-ink-3">{column.heading}</h2>
                <ul className="mt-5 space-y-3">
                  {column.links.map((link) => (
                    <li key={link.href}>
                      <a
                        href={link.href}
                        className="text-sm text-ink-2 transition-colors hover:text-ink focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-brand-800"
                      >
                        {link.label}
                      </a>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </nav>
        </div>

        <div className="mt-14 flex flex-col gap-3 border-t border-hair pt-7 sm:mt-16 sm:flex-row sm:items-center sm:justify-between">
          <p className="font-mono text-[10.5px] uppercase tracking-[0.16em] text-ink-3">
            © 2026 XFORCEMSW. All rights reserved.
          </p>
          <p className="font-mono text-[10.5px] uppercase tracking-[0.16em] text-ink-4">
            Source &rarr; Parser &rarr; Logic model &rarr; Analysis &rarr; Artefacts
          </p>
        </div>
      </div>
    </footer>
  );
}
