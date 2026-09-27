export function PageTitle({
  title,
  description,
  action,
}: {
  title: string;
  description?: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 className="text-xl font-semibold sm:text-2xl">{title}</h1>
        {description ? <p className="mt-1.5 text-sm text-mist-500">{description}</p> : null}
      </div>
      {action}
    </div>
  );
}

export function UsageBar({ percent, label, value }: { percent: number; label: string; value: string }) {
  return (
    <div>
      <div className="flex items-baseline justify-between">
        <p className="text-[11px] font-medium uppercase tracking-[0.14em] text-mist-500">{label}</p>
        <p className="text-xs tabular-nums text-mist-300">{value}</p>
      </div>
      <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-ink-800">
        <div
          className="h-full rounded-full bg-gradient-to-r from-signal-600 to-signal-400 transition-[width]"
          style={{ width: `${Math.max(2, percent)}%` }}
        />
      </div>
    </div>
  );
}
