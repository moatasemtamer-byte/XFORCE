import type { ReactNode } from "react";

export function nx(...classes: Array<string | false | null | undefined>): string {
  return classes.filter(Boolean).join(" ");
}

export function NxPanel({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={nx("border border-nx-line bg-nx-white", className)}>{children}</div>;
}

export function NxPanelTitle({
  title,
  description,
  action,
}: {
  title: ReactNode;
  description?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className="flex items-start justify-between gap-4 border-b border-nx-line px-5 py-3.5">
      <div>
        <h2 className="text-sm font-semibold text-nx-ink">{title}</h2>
        {description ? <p className="mt-1 text-xs text-nx-hint">{description}</p> : null}
      </div>
      {action}
    </div>
  );
}

export function NxStat({
  label,
  value,
  hint,
  tone = "none",
}: {
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  tone?: "none" | "warn" | "bad" | "ok";
}) {
  const toneClass =
    tone === "bad"
      ? "text-nx-fail-fg"
      : tone === "warn"
        ? "text-nx-unv-fg"
        : tone === "ok"
          ? "text-nx-green-700"
          : "text-nx-ink";
  return (
    <div className="border border-nx-line bg-nx-white px-5 py-4">
      <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-nx-hint">{label}</p>
      <p className={nx("mt-2 text-2xl font-semibold tabular-nums", toneClass)}>{value}</p>
      {hint ? <p className="mt-1.5 text-[11px] text-nx-hint">{hint}</p> : null}
    </div>
  );
}

export function NxButton({
  variant = "primary",
  className,
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { variant?: "primary" | "quiet" }) {
  const variantClass =
    variant === "quiet"
      ? "border border-nx-line-strong bg-nx-white text-nx-ink hover:bg-nx-green-50"
      : "bg-nx-ink text-white hover:bg-black";
  return (
    <button
      {...props}
      className={nx(
        "inline-flex items-center gap-2 px-4 py-2 text-xs font-semibold transition-colors disabled:cursor-not-allowed disabled:opacity-40",
        variantClass,
        className,
      )}
    />
  );
}

export const nxInputClass =
  "w-full border border-nx-line-strong bg-nx-white px-3 py-2 text-sm text-nx-ink outline-none placeholder:text-nx-hint focus:border-nx-green-700";

export function NxField({
  label,
  hint,
  children,
}: {
  label: string;
  hint?: string;
  children: ReactNode;
}) {
  return (
    <label className="block">
      <span className="text-[11px] font-semibold uppercase tracking-[0.12em] text-nx-hint">{label}</span>
      <span className="mt-1.5 block">{children}</span>
      {hint ? <span className="mt-1 block text-[11px] text-nx-hint">{hint}</span> : null}
    </label>
  );
}

export function NxPanelBody({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={nx("px-5 py-4", className)}>{children}</div>;
}

export function NxEmptyState({ title, description }: { title: string; description: string }) {
  return (
    <div className="border border-nx-line bg-nx-white px-6 py-10 text-center">
      <p className="text-sm font-semibold text-nx-ink">{title}</p>
      <p className="mx-auto mt-1.5 max-w-md text-xs text-nx-hint">{description}</p>
    </div>
  );
}