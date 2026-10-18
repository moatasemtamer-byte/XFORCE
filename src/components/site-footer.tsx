import Link from "next/link";
import { engineStatus } from "@/lib/converter";
import { NAV_ITEMS, PRODUCT_LINKS } from "@/lib/nav";
import { Logo } from "./logo";

export async function SiteFooter() {
  const engine = await engineStatus();

  return (
    <footer className="border-t border-ink-800 bg-ink-900/40">
      <div className="mx-auto max-w-6xl px-5 py-12">
        <div className="grid gap-10 md:grid-cols-4">
          <div className="md:col-span-2">
            <Logo />
            <p className="mt-4 max-w-sm text-sm leading-relaxed text-mist-500">
              XforceMsw builds the conversion pipeline that turns PLC ladder logic into Triconex
              TRISTATION-MS Workstation notation, and ships it as a licensed, auditable web service.
            </p>
          </div>

          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.16em] text-mist-500">Product</p>
            <ul className="mt-4 space-y-2.5">
              {NAV_ITEMS.map((item) => (
                <li key={item.href}>
                  <Link href={item.href} className="text-sm text-mist-400 transition-colors hover:text-mist-100">
                    {item.label}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.16em] text-mist-500">Account</p>
            <ul className="mt-4 space-y-2.5">
              {PRODUCT_LINKS.map((item) => (
                <li key={item.href}>
                  <Link href={item.href} className="text-sm text-mist-400 transition-colors hover:text-mist-100">
                    {item.label}
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        </div>

        <div className="mt-10 h-px rule" />
        <div className="mt-6 flex flex-col items-start justify-between gap-3 text-xs text-mist-500 sm:flex-row">
          <p>© {new Date().getFullYear()} XforceMsw. All rights reserved.</p>
          <p className="flex items-center gap-2">
            <span
              className={engine.ready ? "h-1.5 w-1.5 rounded-full bg-good-500" : "h-1.5 w-1.5 rounded-full bg-amberline-400"}
            />
            {engine.ready ? `Conversion engine online` : "Conversion engine not connected"}
          </p>
        </div>
      </div>
    </footer>
  );
}
