import Link from "next/link";
import { SiteFooter } from "./site-footer";
import { SiteHeader } from "./site-header";

export function MarketingShell({
  children,
  className,
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={className ? `flex min-h-full flex-col ${className}` : "flex min-h-full flex-col"}>
      <SiteHeader />
      <main className="flex-1">{children}</main>
      <SiteFooter />
    </div>
  );
}

export function PageHeader({
  eyebrow,
  title,
  description,
}: {
  eyebrow: string;
  title: string;
  description: string;
}) {
  return (
    <section className="relative overflow-hidden border-b border-ink-800">
      <div className="blueprint absolute inset-0 opacity-50" />
      <div className="absolute inset-0 bg-gradient-to-b from-ink-950/40 to-ink-950" />
      <div className="relative mx-auto max-w-6xl px-5 py-16">
        <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-signal-400">{eyebrow}</p>
        <h1 className="mt-3 max-w-3xl text-3xl font-semibold sm:text-4xl">{title}</h1>
        <p className="mt-4 max-w-2xl text-[15px] leading-relaxed text-mist-400">{description}</p>
      </div>
    </section>
  );
}

export function Crumb({ items }: { items: { href: string; label: string }[] }) {
  return (
    <nav aria-label="Breadcrumb" className="text-xs text-mist-500">
      <ol className="flex flex-wrap items-center gap-1.5">
        {items.map((item, index) => (
          <li key={item.href} className="flex items-center gap-1.5">
            {index > 0 ? <span className="text-ink-400">/</span> : null}
            <Link href={item.href} className="transition-colors hover:text-mist-200">
              {item.label}
            </Link>
          </li>
        ))}
      </ol>
    </nav>
  );
}
