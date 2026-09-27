import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";
import { Logo } from "@/components/logo";
import { DashboardSidebar, MobileSidebarNav } from "@/components/dashboard/sidebar";
import { LogoutButton } from "@/components/dashboard/logout-button";
import { getCurrentUser } from "@/lib/auth";
import { listLicenses, effectivePlan } from "@/lib/licensing";
import { engineStatus } from "@/lib/converter";

export const metadata: Metadata = {
  title: "Dashboard",
  robots: { index: false, follow: false },
};

export default async function DashboardLayout({ children }: LayoutProps<"/dashboard">) {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const licenses = listLicenses(user.id);
  const plan = effectivePlan(user, licenses);
  const engine = await engineStatus();

  return (
    <div className="flex min-h-full flex-1 flex-col">
      <header className="sticky top-0 z-40 border-b border-ink-800 bg-ink-950/90 backdrop-blur-md">
        <div className="flex h-14 items-center justify-between gap-4 px-4 sm:px-6">
          <Link href="/dashboard">
            <Logo />
          </Link>

          <div className="flex items-center gap-3">
            <span
              className="hidden items-center gap-1.5 text-[11px] text-mist-500 sm:flex"
              title={engine.detail}
            >
              <span
                className={
                  engine.ready ? "h-1.5 w-1.5 rounded-full bg-good-500" : "h-1.5 w-1.5 rounded-full bg-amberline-400"
                }
              />
              {engine.ready ? "Engine online" : "Engine pending"}
            </span>

            <div className="text-right">
              <p className="text-xs font-medium text-mist-200">{user.name}</p>
              <p className="text-[11px] text-mist-500">{user.email}</p>
            </div>

            <span
              className={
                user.role === "admin"
                  ? "rounded-full border border-signal-500/40 bg-signal-500/10 px-2 py-0.5 text-[10px] font-medium text-signal-300"
                  : "hidden rounded-full border border-ink-600 px-2 py-0.5 text-[10px] text-mist-500 sm:inline"
              }
            >
              {user.role}
            </span>

            <LogoutButton />
          </div>
        </div>
      </header>

      <MobileSidebarNav isAdmin={user.role === "admin"} />

      <div className="flex flex-1">
        <DashboardSidebar isAdmin={user.role === "admin"} plan={plan.name} />
        <main className="min-w-0 flex-1 px-4 py-6 sm:px-6 sm:py-8">{children}</main>
      </div>
    </div>
  );
}
