import Link from "next/link";
import { buttonClass } from "@/components/ui";

export default function NotFound() {
  return (
    <div className="relative flex min-h-full flex-1 items-center justify-center overflow-hidden">
      <div className="blueprint absolute inset-0 opacity-50" />
      <div className="absolute inset-0 bg-gradient-to-b from-ink-950/40 to-ink-950" />

      <div className="relative px-5 text-center">
        <p className="font-mono text-5xl font-semibold text-signal-400">404</p>
        <h1 className="mt-4 text-2xl font-semibold">This rung is not connected</h1>
        <p className="mx-auto mt-3 max-w-md text-sm leading-relaxed text-mist-400">
          The page you asked for does not exist, or the job, license or account behind it is not
          visible to you.
        </p>
        <div className="mt-8 flex flex-wrap justify-center gap-3">
          <Link href="/" className={buttonClass("primary", "md")}>
            Back to home
          </Link>
          <Link href="/dashboard" className={buttonClass("outline", "md")}>
            Go to dashboard
          </Link>
        </div>
      </div>
    </div>
  );
}
