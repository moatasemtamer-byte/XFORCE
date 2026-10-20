import Link from "next/link";
import type { ComponentProps, ReactNode } from "react";

export function cx(...values: (string | false | null | undefined)[]): string {
  return values.filter(Boolean).join(" ");
}

type Variant = "primary" | "ghost" | "outline" | "danger" | "subtle";
type Size = "sm" | "md" | "lg";

const VARIANTS: Record<Variant, string> = {
  primary: "bg-nx-ink text-white hover:bg-black font-semibold",
  ghost: "text-nx-body hover:text-nx-ink hover:bg-nx-green-50",
  outline: "border border-nx-line-strong text-nx-ink hover:border-nx-green-700 hover:bg-nx-green-50",
  subtle: "bg-nx-green-50 text-nx-ink hover:bg-nx-green-100 border border-nx-line",
  danger: "border border-nx-fail-fg/50 text-nx-fail-fg hover:bg-nx-fail-bg",
};

const SIZES: Record<Size, string> = {
  sm: "h-8 px-3 text-xs gap-1.5",
  md: "h-10 px-4 text-sm gap-2",
  lg: "h-12 px-6 text-[15px] gap-2.5",
};

export function buttonClass(variant: Variant = "primary", size: Size = "md", className?: string): string {
  return cx(
    "inline-flex items-center justify-center rounded-none transition-colors duration-150 select-none",
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
    <div className={cx("border border-nx-line bg-nx-white", className)} {...props}>
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
    <div className="flex flex-wrap items-start justify-between gap-3 border-b border-nx-line px-5 py-4">
      <div>
        <h2 className="text-sm font-semibold text-nx-ink">{title}</h2>
        {description ? <p className="mt-1 text-xs text-nx-hint">{description}</p> : null}
      </div>
      {action}
    </div>
  );
}

export type Tone = "neutral" | "good" | "warn" | "bad" | "info";

const TONES: Record<Tone, string> = {
  neutral: "border-nx-line-strong bg-nx-paper text-nx-body",
  good: "border-nx-green-700/40 bg-nx-green-50 text-nx-green-700",
  warn: "border-nx-unv-fg/40 bg-nx-unv-bg text-nx-unv-fg",
  bad: "border-nx-fail-fg/40 bg-nx-fail-bg text-nx-fail-fg",
  info: "border-nx-ns-fg/40 bg-nx-ns-bg text-nx-ns-fg",
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
        "inline-flex items-center gap-1.5 rounded-none border px-2.5 py-0.5 text-[11px] font-medium tracking-wide",
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
      ? "text-nx-green-700"
      : tone === "warn"
        ? "text-nx-unv-fg"
        : tone === "bad"
          ? "text-nx-fail-fg"
          : tone === "info"
            ? "text-nx-ns-fg"
            : "text-nx-ink";

  return (
    <div className="border border-nx-line bg-nx-white px-5 py-4">
      <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-nx-hint">{label}</p>
      <p className={cx("mt-2 text-2xl font-semibold tabular-nums", accent)}>{value}</p>
      {hint ? <p className="mt-1 text-xs text-nx-hint">{hint}</p> : null}
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
        <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-nx-green-700">{eyebrow}</p>
      ) : null}
      <h2 className="mt-3 text-2xl font-semibold text-nx-ink sm:text-3xl">{title}</h2>
      {description ? <p className="mt-3 text-[15px] leading-relaxed text-nx-body">{description}</p> : null}
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
      <div className="grid h-11 w-11 place-items-center border border-nx-line-strong bg-nx-paper text-nx-hint">
        <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.6">
          <path d="M4 7h16M4 12h10M4 17h6" strokeLinecap="round" />
        </svg>
      </div>
      <h3 className="mt-4 text-sm font-semibold text-nx-ink">{title}</h3>
      <p className="mt-1.5 max-w-sm text-xs leading-relaxed text-nx-hint">{description}</p>
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
      <label htmlFor={htmlFor} className="block text-[11px] font-semibold uppercase tracking-[0.12em] text-nx-hint">
        {label}
      </label>
      <div className="mt-1.5">{children}</div>
      {error ? (
        <p className="mt-1.5 text-xs text-nx-fail-fg">{error}</p>
      ) : hint ? (
        <p className="mt-1.5 text-xs text-nx-hint">{hint}</p>
      ) : null}
    </div>
  );
}

export const inputClass =
  "w-full rounded-none border border-nx-line-strong bg-nx-white px-3 py-2 text-sm text-nx-ink placeholder:text-nx-hint transition-colors focus:border-nx-green-700 focus:outline-none";
