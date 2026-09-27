import Link from "next/link";
import type { ComponentProps, ReactNode } from "react";

export function cx(...values: (string | false | null | undefined)[]): string {
  return values.filter(Boolean).join(" ");
}

type Variant = "primary" | "ghost" | "outline" | "danger" | "subtle";
type Size = "sm" | "md" | "lg";

const VARIANTS: Record<Variant, string> = {
  primary:
    "bg-signal-500 text-ink-950 hover:bg-signal-400 focus-visible:outline-signal-400 font-semibold shadow-[0_8px_24px_-12px_var(--color-signal-500)]",
  ghost: "text-mist-300 hover:text-mist-100 hover:bg-ink-800",
  outline: "border border-ink-600 text-mist-200 hover:border-signal-500/60 hover:text-mist-100 bg-ink-900/40",
  subtle: "bg-ink-800 text-mist-200 hover:bg-ink-700 border border-ink-700",
  danger: "border border-bad-500/50 text-bad-500 hover:bg-bad-500/10",
};

const SIZES: Record<Size, string> = {
  sm: "h-8 px-3 text-xs gap-1.5",
  md: "h-10 px-4 text-sm gap-2",
  lg: "h-12 px-6 text-[15px] gap-2.5",
};

export function buttonClass(variant: Variant = "primary", size: Size = "md", className?: string): string {
  return cx(
    "inline-flex items-center justify-center rounded-lg transition-colors duration-150 select-none",
    "focus-visible:outline-2 focus-visible:outline-offset-2 disabled:opacity-50 disabled:pointer-events-none whitespace-nowrap",
    VARIANTS[variant],
    SIZES[size],
    className,
  );
}

export function Button({
  variant = "primary",
  size = "md",
  className,
  ...props
}: ComponentProps<"button"> & { variant?: Variant; size?: Size }) {
  return <button className={buttonClass(variant, size, className)} {...props} />;
}

export function ButtonLink({
  variant = "primary",
  size = "md",
  className,
  ...props
}: ComponentProps<typeof Link> & { variant?: Variant; size?: Size }) {
  return <Link className={buttonClass(variant, size, className)} {...props} />;
}

export function Panel({
  className,
  children,
  ...props
}: ComponentProps<"div">) {
  return (
    <div className={cx("panel", className)} {...props}>
      {children}
    </div>
  );
}

export function PanelTitle({
  title,
  description,
  action,
}: {
  title: ReactNode;
  description?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-3 border-b border-ink-700 px-5 py-4">
      <div>
        <h2 className="text-sm font-semibold text-mist-100">{title}</h2>
        {description ? <p className="mt-1 text-xs text-mist-500">{description}</p> : null}
      </div>
      {action}
    </div>
  );
}

export type Tone = "neutral" | "good" | "warn" | "bad" | "info";

const TONES: Record<Tone, string> = {
  neutral: "border-ink-600 bg-ink-800 text-mist-300",
  good: "border-good-500/40 bg-good-500/10 text-good-500",
  warn: "border-amberline-500/40 bg-amberline-500/10 text-amberline-400",
  bad: "border-bad-500/40 bg-bad-500/10 text-bad-500",
  info: "border-signal-500/40 bg-signal-500/10 text-signal-300",
};

export function Badge({
  tone = "neutral",
  className,
  children,
}: {
  tone?: Tone;
  className?: string;
  children: ReactNode;
}) {
  return (
    <span
      className={cx(
        "inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium tracking-wide",
        TONES[tone],
        className,
      )}
    >
      {children}
    </span>
  );
}

export function Stat({
  label,
  value,
  hint,
  tone = "neutral",
}: {
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  tone?: Tone;
}) {
  const accent =
    tone === "good"
      ? "text-good-500"
      : tone === "warn"
        ? "text-amberline-400"
        : tone === "bad"
          ? "text-bad-500"
          : tone === "info"
            ? "text-signal-300"
            : "text-mist-100";

  return (
    <div className="panel-raised px-5 py-4">
      <p className="text-[11px] font-medium uppercase tracking-[0.14em] text-mist-500">{label}</p>
      <p className={cx("mt-2 text-2xl font-semibold tabular-nums", accent)}>{value}</p>
      {hint ? <p className="mt-1 text-xs text-mist-500">{hint}</p> : null}
    </div>
  );
}

export function SectionHeading({
  eyebrow,
  title,
  description,
  align = "center",
  className,
}: {
  eyebrow?: string;
  title: ReactNode;
  description?: ReactNode;
  align?: "center" | "left";
  className?: string;
}) {
  return (
    <div
      className={cx(
        "max-w-2xl",
        align === "center" ? "mx-auto text-center" : "",
        className,
      )}
    >
      {eyebrow ? (
        <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-signal-400">{eyebrow}</p>
      ) : null}
      <h2 className="mt-3 text-2xl font-semibold sm:text-3xl">{title}</h2>
      {description ? <p className="mt-3 text-[15px] leading-relaxed text-mist-400">{description}</p> : null}
    </div>
  );
}

export function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-col items-center justify-center px-6 py-16 text-center">
      <div className="grid h-11 w-11 place-items-center rounded-xl border border-ink-600 bg-ink-850 text-mist-500">
        <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.6">
          <path d="M4 7h16M4 12h10M4 17h6" strokeLinecap="round" />
        </svg>
      </div>
      <h3 className="mt-4 text-sm font-semibold text-mist-200">{title}</h3>
      <p className="mt-1.5 max-w-sm text-xs leading-relaxed text-mist-500">{description}</p>
      {action ? <div className="mt-5">{action}</div> : null}
    </div>
  );
}

export function Field({
  label,
  htmlFor,
  hint,
  error,
  children,
}: {
  label: string;
  htmlFor: string;
  hint?: string;
  error?: string;
  children: ReactNode;
}) {
  return (
    <div>
      <label htmlFor={htmlFor} className="block text-xs font-medium text-mist-300">
        {label}
      </label>
      <div className="mt-1.5">{children}</div>
      {error ? (
        <p className="mt-1.5 text-xs text-bad-500">{error}</p>
      ) : hint ? (
        <p className="mt-1.5 text-xs text-mist-500">{hint}</p>
      ) : null}
    </div>
  );
}

export const inputClass =
  "w-full rounded-lg border border-ink-600 bg-ink-900/70 px-3 py-2 text-sm text-mist-100 placeholder:text-mist-500 transition-colors focus:border-signal-500 focus:outline-none focus:ring-1 focus:ring-signal-500/40";
