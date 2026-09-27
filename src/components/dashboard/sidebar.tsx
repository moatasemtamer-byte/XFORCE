"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cx } from "@/components/ui";

interface Item {
  href: string;
  label: string;
  icon: string;
}

const MAIN: Item[] = [
  { href: "/dashboard", label: "Overview", icon: "M4 13h6V4H4v9zm0 7h6v-4H4v4zm10 0h6v-9h-6v9zm0-16v4h6V4h-6z" },
  { href: "/dashboard/convert", label: "Convert", icon: "M12 5v14m0-14l-4 4m4-4l4 4M5 19h14" },
  { href: "/dashboard/jobs", label: "Jobs", icon: "M4 6h16M4 12h16M4 18h10" },
  { href: "/dashboard/licenses", label: "Licenses", icon: "M15 7a4 4 0 100 8H8v3l-2-2 2-2v3h7a4 4 0 000-8h-1z" },
  { href: "/dashboard/billing", label: "Billing", icon: "M3 7h18v10H3V7zm0 4h18" },
  { href: "/dashboard/settings", label: "Settings", icon: "M12 15a3 3 0 100-6 3 3 0 000 6z" },
];

const ADMIN: Item[] = [
  { href: "/dashboard/admin", label: "Admin", icon: "M12 3l7 4v5c0 4.4-3 8-7 9-4-1-7-4.6-7-9V7l7-4z" },
];

function NavLink({ item, active }: { item: Item; active: boolean }) {
  return (
    <Link
      href={item.href}
      className={cx(
        "flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition-colors",
        active
          ? "bg-ink-800 text-mist-100 shadow-[inset_2px_0_0_0_var(--color-signal-400)]"
          : "text-mist-400 hover:bg-ink-850 hover:text-mist-200",
      )}
    >
      <svg viewBox="0 0 24 24" className="h-4 w-4 shrink-0" fill="none" stroke="currentColor" strokeWidth="1.7">
        <path d={item.icon} strokeLinecap="round" strokeLinejoin="round" />
      </svg>
      {item.label}
    </Link>
  );
}

export function DashboardSidebar({ isAdmin, plan }: { isAdmin: boolean; plan: string }) {
  const pathname = usePathname();

  const isActive = (href: string) =>
    href === "/dashboard" ? pathname === href : pathname === href || pathname.startsWith(`${href}/`);

  return (
    <aside className="hidden w-60 shrink-0 border-r border-ink-800 bg-ink-900/40 lg:flex lg:flex-col">
      <nav className="flex-1 space-y-1 p-4">
        <p className="px-3 pb-2 text-[10px] font-semibold uppercase tracking-[0.18em] text-ink-400">Workspace</p>
        {MAIN.map((item) => (
          <NavLink key={item.href} item={item} active={isActive(item.href)} />
        ))}

        {isAdmin ? (
          <>
            <p className="px-3 pb-2 pt-6 text-[10px] font-semibold uppercase tracking-[0.18em] text-ink-400">
              Administration
            </p>
            {ADMIN.map((item) => (
              <NavLink key={item.href} item={item} active={isActive(item.href)} />
            ))}
          </>
        ) : null}
      </nav>

      <div className="border-t border-ink-800 p-4">
        <div className="rounded-lg border border-ink-700 bg-ink-850 p-3">
          <p className="text-[10px] font-semibold uppercase tracking-[0.16em] text-ink-400">Current plan</p>
          <p className="mt-1 text-sm font-semibold capitalize text-signal-300">{plan}</p>
          <Link
            href="/dashboard/billing"
            className="mt-2.5 block text-xs text-mist-400 underline-offset-4 hover:text-mist-100 hover:underline"
          >
            Manage plan
          </Link>
        </div>
      </div>
    </aside>
  );
}

export function MobileSidebarNav({ isAdmin }: { isAdmin: boolean }) {
  const pathname = usePathname();
  const items = isAdmin ? [...MAIN, ...ADMIN] : MAIN;

  return (
    <div className="flex gap-1 overflow-x-auto border-b border-ink-800 px-4 py-2 lg:hidden">
      {items.map((item) => {
        const active =
          item.href === "/dashboard" ? pathname === item.href : pathname.startsWith(item.href);
        return (
          <Link
            key={item.href}
            href={item.href}
            className={cx(
              "shrink-0 rounded-full border px-3 py-1.5 text-xs transition-colors",
              active
                ? "border-signal-500/50 bg-signal-500/10 text-signal-300"
                : "border-ink-700 text-mist-400",
            )}
          >
            {item.label}
          </Link>
        );
      })}
    </div>
  );
}
