import Link from "next/link";
import { Logo } from "./logo";
import { NxSplash } from "./nx/splash";

export function AuthShell({
  title,
  subtitle,
  notice,
  children,
  footer,
}: {
  title: string;
  subtitle: string;
  notice?: React.ReactNode;
  children: React.ReactNode;
  footer: React.ReactNode;
}) {
  return (
    <div className="nx-app flex min-h-screen flex-col">
      <NxSplash />

      <div className="mx-auto flex w-full max-w-md flex-1 flex-col justify-center px-5 py-16">
        <Link href="/" className="mb-8 inline-flex">
          <Logo />
        </Link>

        <div className="border border-nx-line bg-nx-white p-7">
          <h1 className="text-xl font-semibold text-nx-ink">{title}</h1>
          <p className="mt-1.5 text-xs text-nx-hint">{subtitle}</p>
          {notice ? <div className="mt-5">{notice}</div> : null}
          <div className="mt-6">{children}</div>
        </div>

        <div className="mt-6 text-center text-xs text-nx-hint">{footer}</div>
      </div>
    </div>
  );
}
