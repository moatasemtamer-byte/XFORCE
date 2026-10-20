import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";
import { Logo } from "@/components/logo";
import { NxSplash } from "@/components/nx/splash";
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
    <div className="nx-app flex min-h-screen flex-col">
      <NxSplash />

      <header className="sticky top-0 z-40 border-b border-nx-line bg-nx-white">
        <div className="flex items-center justify-between gap-4 px-4 py-2.5 sm:px-6 lg:px-10">
          <Link href="/dashboard" aria-label="Home">
            <Logo />
          </Link>

          <div className="flex items-center gap-3">
            <span
              className="hidden items-center gap-1.5 text-[11px] text-nx-hint sm:flex"
              title={engine.detail}
            >
              <span
                className={
                  engine.ready ? "h-1.5 w-1.5 rounded-full bg-nx-green-700" : "h-1.5 w-1.5 rounded-full bg-nx-unv-fg"
                }
              />
              {engine.ready ? "Engine online" : "Engine pending"}
            </span>

            <div className="text-right">
              <p className="text-xs font-medium text-nx-ink">{user.name}</p>
              <p className="text-[11px] text-nx-hint">{user.email}</p>
            </div>

            <span
              className={
                user.role === "admin"
                  ? "rounded-none border border-nx-fail-fg/40 bg-nx-fail-bg px-2 py-0.5 text-[10px] font-medium uppercase tracking-wide text-nx-fail-fg"
                  : "hidden rounded-none border border-nx-line-strong px-2 py-0.5 text-[10px] uppercase tracking-wide text-nx-hint sm:inline"
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
        <main className="min-w-0 flex-1 px-4 py-6 sm:px-6 sm:py-8 lg:px-10">{children}</main>
      </div>
    </div>
  );
}
