"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

/**
 * Section rail. Each entry is a rung: a wire runs from the 3px rail to a
 * marker that lights neon on the active screen. Screens that are not built yet
 * stay visible but inert — never a link to a 404.
 */
interface NavItem {
  href: string;
  label: string;
  ready: boolean;
}

const ITEMS: NavItem[] = [
  { href: "/engine/convert", label: "Convert", ready: true },
  { href: "/engine/viewer", label: "Project viewer", ready: true },
  { href: "/engine", label: "Dashboard", ready: true },
  { href: "/engine/report", label: "Report", ready: false },
  { href: "/engine/library", label: "Library calls", ready: false },
  { href: "/engine/addresses", label: "Address audit", ready: false },
  { href: "/engine/names", label: "Names & identifiers", ready: false },
  { href: "/engine/goldens", label: "Goldens & capacity", ready: false },
  { href: "/engine/tia", label: "TIA connection", ready: false },
];

export function NxSidebar() {
  const pathname = usePathname();

  return (
    <nav aria-label="Nexus sections" className="border-l-[3px] border-nx-line">
      <ul className="space-y-0.5 py-2">
        {ITEMS.map((item) => {
          const active = item.ready && (pathname === item.href || pathname === `${item.href}/`);
          const content = (
            <>
              <span
                aria-hidden="true"
                className={`absolute left-[-8px] top-1/2 h-2 w-2 -translate-x-1/2 -translate-y-1/2 ${
                  active ? "bg-nx-neon" : "bg-nx-line-strong"
                }`}
              />
              <span
                aria-hidden="true"
                className={`absolute left-[-3px] top-1/2 h-[3px] w-2 -translate-y-1/2 ${
                  active ? "bg-nx-neon" : "bg-nx-line-strong"
                }`}
              />
              <span className="flex-1 truncate">{item.label}</span>
              {item.ready ? null : (
                <span className="text-[9px] font-semibold uppercase tracking-wide text-nx-hint">soon</span>
              )}
            </>
          );

          return (
            <li key={item.href} className="relative">
              {item.ready ? (
                <Link
                  href={item.href}
                  aria-current={active ? "page" : undefined}
                  className={`relative flex items-center gap-2 py-2 pl-6 pr-3 text-sm transition-colors ${
                    active
                      ? "bg-nx-green-100 font-semibold text-nx-ink"
                      : "text-nx-body hover:bg-nx-green-50 hover:text-nx-ink"
                  }`}
                >
                  {content}
                </Link>
              ) : (
                <span
                  aria-disabled="true"
                  className="relative flex items-center gap-2 py-2 pl-6 pr-3 text-sm text-nx-hint"
                >
                  {content}
                </span>
              )}
            </li>
          );
        })}
      </ul>
    </nav>
  );
}