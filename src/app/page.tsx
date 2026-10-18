import type { Metadata, Viewport } from "next";
import { appUrl, siteName } from "@/lib/env";
import { BrandLogo } from "@/components/landing/brand-logo";
import { RevealBootstrap, RevealObserver } from "@/components/landing/client";
import { CtaSection, LandingFooter } from "@/components/landing/cta-footer";
import { Hero } from "@/components/landing/hero";
import { LandingNav } from "@/components/landing/landing-nav";
import {
  AboutSection,
  AccessSection,
  ArchitectureSection,
  ArtefactsSection,
  BoundariesSection,
  ComparisonSection,
  CrossReferenceSection,
  DeterministicSection,
  ModernizationSection,
  PipelineSection,
  PlansSection,
  ProblemSection,
  ReviewSection,
  StructureSection,
} from "@/components/landing/sections";

const TITLE = "From ladder logic to structured engineering data";
const DESCRIPTION =
  "XFORCEMSW reads industrial control logic from text-based PDFs and ASCII listings, reconstructs its structure, generates Boolean equations, cross-references signals, reports analysis findings for review, and exports engineering artefacts — offline, deterministically, from one source.";

export const metadata: Metadata = {
  title: `${TITLE} · ${siteName()}`,
  description: DESCRIPTION,
  alternates: { canonical: appUrl() ? "/" : undefined },
  openGraph: {
    title: `${siteName()} — ${TITLE}`,
    description: DESCRIPTION,
    type: "website",
    siteName: siteName(),
    locale: "en",
    images: [
      {
        url: "/brand/xforcemsw-logo.png",
        width: 1983,
        height: 793,
        alt: "XFORCEMSW — industrial logic, reconstructed",
      },
    ],
  },
  twitter: {
    card: "summary_large_image",
    title: `${siteName()} — ${TITLE}`,
    description: DESCRIPTION,
    images: ["/brand/xforcemsw-logo.png"],
  },
};

export const viewport: Viewport = {
  themeColor: "#ffffff",
  colorScheme: "light",
};

const ORG_ID = "https://xforcemsw.com/#organization";

export default function LandingPage() {
  const structuredData = {
    "@context": "https://schema.org",
    "@graph": [
      {
        "@type": "Organization",
        "@id": ORG_ID,
        name: "XFORCEMSW",
        url: appUrl() || "https://xforcemsw.com",
        logo: {
          "@type": "ImageObject",
          url: `${appUrl() || "https://xforcemsw.com"}/brand/xforcemsw-logo.png`,
          width: 1983,
          height: 793,
        },
        slogan: "Industrial logic, reconstructed",
        description: DESCRIPTION,
      },
      {
        "@type": "WebSite",
        "@id": "https://xforcemsw.com/#website",
        url: appUrl() || "https://xforcemsw.com",
        name: "XFORCEMSW",
        description: DESCRIPTION,
        publisher: { "@id": ORG_ID },
        inLanguage: "en",
      },
    ],
  };

  return (
    <div className="surface-light flex min-h-screen flex-col bg-paper">
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(structuredData) }} />
      <RevealBootstrap />
      <RevealObserver />

      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-100 focus:h-auto focus:w-auto focus:rounded-none focus:border focus:border-ink focus:bg-paper focus:px-4 focus:py-3 focus:text-xs focus:font-semibold focus:uppercase focus:tracking-[0.14em] focus:text-ink focus:[clip:auto]"
      >
        Skip to content
      </a>

      <LandingNav />

      <main id="main" className="flex-1">
        <Hero />
        <ProblemSection />
        <PipelineSection />
        <StructureSection />
        <CrossReferenceSection />
        <ReviewSection />
        <ArchitectureSection />
        <ComparisonSection />
        <DeterministicSection />
        <ArtefactsSection />
        <BoundariesSection />
        <ModernizationSection />
        <AccessSection />
        <PlansSection />
        <AboutSection />
        <CtaSection />
      </main>

      <LandingFooter />

      {/* Wordmark kept out of the visual flow; it gives search engines and
          assistive tech the brand name without a second visible image. */}
      <span className="sr-only">
        <BrandLogo size="sm" />
      </span>
    </div>
  );
}
