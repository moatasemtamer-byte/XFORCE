import Link from "next/link";
import { Logo } from "./logo";

export function AuthShell({
  title,
  subtitle,
  children,
  footer,
}: {
  title: string;
  subtitle: string;
  children: React.ReactNode;
  footer: React.ReactNode;
}) {
  return (
    <div className="relative flex min-h-full flex-col">
      <div className="blueprint absolute inset-0 opacity-40" />
      <div className="absolute inset-0 bg-gradient-to-b from-ink-950/30 to-ink-950" />

      <div className="relative mx-auto flex w-full max-w-md flex-1 flex-col justify-center px-5 py-16">
        <Link href="/" className="mb-8 inline-flex">
          <Logo />
        </Link>

        <div className="panel-raised p-7">
          <h1 className="text-xl font-semibold">{title}</h1>
          <p className="mt-1.5 text-xs text-mist-500">{subtitle}</p>
          <div className="mt-6">{children}</div>
        </div>

        <div className="mt-6 text-center text-xs text-mist-500">{footer}</div>
      </div>
    </div>
  );
}
