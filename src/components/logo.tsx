export function Logo({ className = "" }: { className?: string }) {
  return (
    <span className={`inline-flex items-center gap-2.5 ${className}`}>
      <span className="relative grid h-8 w-8 place-items-center rounded-lg border border-ink-600 bg-ink-850">
        <svg viewBox="0 0 24 24" className="h-4.5 w-4.5" aria-hidden="true">
          <path d="M3 6h4M3 18h4" stroke="var(--color-signal-400)" strokeWidth="2" strokeLinecap="round" />
          <path d="M7 6h3v12h3" stroke="var(--color-mist-100)" strokeWidth="2" strokeLinecap="round" fill="none" />
          <rect x="13" y="4.5" width="4" height="4" rx="0.8" stroke="var(--color-amberline-400)" strokeWidth="1.6" fill="none" />
          <rect x="13" y="15.5" width="4" height="4" rx="2" stroke="var(--color-signal-300)" strokeWidth="1.6" fill="none" />
          <path d="M15 8.5v7" stroke="var(--color-mist-400)" strokeWidth="1.6" strokeLinecap="round" />
          <path d="M21 6h-1.5M21 18h-1.5" stroke="var(--color-ink-400)" strokeWidth="2" strokeLinecap="round" />
        </svg>
      </span>
      <span className="font-semibold tracking-tight text-mist-100">XforceMsw</span>
    </span>
  );
}
