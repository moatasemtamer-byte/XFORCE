"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

/**
 * Section rail. Each entry is a rung: a wire runs from the 3px rail to a
 * marker that lights neon on the active screen.
 */
interface NavItem {
  href: string;
  label: string;
}

const ITEMS: NavItem[] = [
  { href: "/engine/convert", label: "Convert" },
  { href: "/engine/viewer", label: "Project viewer" },
  { href: "/engine", label: "Dashboard" },
];

export function NxSidebar() {
  const pathname = usePathname();

  return (
    <nav aria-label="Nexus sections" className="border-l-[3px] border-nx-line">
      <ul className="space-y-0.5 py-2">
        {ITEMS.map((item) => {
          const active = pathname === item.href || pathname === `${item.href}/`;
          return (
            <li key={item.href} className="relative">
              <Link
                href={item.href}
                aria-current={active ? "page" : undefined}
                className={`relative flex items-center gap-2 py-2 pl-6 pr-3 text-sm transition-colors ${
                  active
                    ? "bg-nx-green-100 font-semibold text-nx-ink"
                    : "text-nx-body hover:bg-nx-green-50 hover:text-nx-ink"
                }`}
              >
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
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}