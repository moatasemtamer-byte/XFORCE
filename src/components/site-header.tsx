import Link from "next/link";
import { getCurrentPublicUser } from "@/lib/auth";
import { NAV_ITEMS } from "@/lib/nav";
import { Logo } from "./logo";
import { MobileNav } from "./mobile-nav";
import { ButtonLink } from "./ui";

export async function SiteHeader() {
  const user = await getCurrentPublicUser();

  return (
    <header className="sticky top-0 z-50 border-b border-ink-800 bg-ink-950/85 backdrop-blur-md">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-6 px-5">
        <Link href="/" className="shrink-0">
          <Logo />
        </Link>

        <nav className="hidden items-center gap-1 md:flex">
          {NAV_ITEMS.map((item) => (
            <Link
              key={item.href}
              href={item.href}
              className="rounded-lg px-3 py-2 text-sm text-mist-400 transition-colors hover:bg-ink-850 hover:text-mist-100"
            >
              {item.label}
            </Link>
          ))}
        </nav>

        <div className="flex items-center gap-2">
          {user ? (
            <>
              <span className="hidden text-xs text-mist-500 lg:inline">
                {user.name} · <span className="capitalize text-signal-300">{user.plan}</span>
              </span>
              <ButtonLink href="/dashboard" size="sm">
                Dashboard
              </ButtonLink>
            </>
          ) : (
            <>
              <ButtonLink href="/login" variant="ghost" size="sm" className="hidden sm:inline-flex">
                Sign in
              </ButtonLink>
              <ButtonLink href="/register" size="sm">
                Start free
              </ButtonLink>
            </>
          )}
          <MobileNav items={NAV_ITEMS} />
        </div>
      </div>
    </header>
  );
}
