import Link from "next/link";
import { NxBrand } from "@/components/nx/brand";
import { NxFooterBand } from "@/components/nx/footer-band";
import { NxScreenTitle } from "@/components/nx/screen-title";
import { NxSidebar } from "@/components/nx/sidebar";
import { NxSplash } from "@/components/nx/splash";
import { getEngineStatus } from "@/lib/engine";

// The evidence workbench is deliberately separate from the SaaS dashboard: it
// reads engine evidence and never touches licensing or billing.
export const dynamic = "force-dynamic";

export default async function EngineLayout({ children }: { children: React.ReactNode }) {
  const { status, error } = await getEngineStatus();

  return (
    <div className="nx-app flex min-h-screen flex-col">
      <NxSplash />

      <header className="sticky top-0 z-30 border-b border-nx-line bg-nx-white">
        <div className="flex w-full items-center gap-3 px-6 py-2.5 lg:px-10">
          <Link href="/" className="shrink-0" aria-label="Home">
            <NxBrand />
          </Link>
          <span aria-hidden="true" className="hidden h-5 w-px bg-nx-line sm:block" />
          <NxScreenTitle />
          <nav className="ml-auto flex items-center gap-4">
            <Link
              href="/"
              className="text-xs text-nx-hint transition-colors hover:text-nx-ink"
            >
              Home
            </Link>
            <Link
              href="/dashboard"
              className="text-xs text-nx-hint transition-colors hover:text-nx-ink"
            >
              Dashboard
            </Link>
          </nav>
        </div>
      </header>

      <div className="flex w-full flex-1 gap-0 px-6 lg:px-10">
        <aside className="sticky top-[49px] hidden max-h-[calc(100vh-49px)] w-60 shrink-0 overflow-y-auto border-r border-nx-line py-5 pr-3 md:block">
          <NxSidebar />
        </aside>
        <main className="min-w-0 flex-1 py-6 md:pl-10">{children}</main>
      </div>

      <NxFooterBand status={status} error={error} />
    </div>
  );
}