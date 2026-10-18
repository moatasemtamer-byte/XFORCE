import type { CSSProperties, ReactNode } from "react";

/** Wraps content so it fades up the first time it enters the viewport. */
export function Reveal({
  children,
  delay = 0,
  className = "",
  as: Tag = "div",
}: {
  children: ReactNode;
  delay?: number;
  className?: string;
  as?: "div" | "li" | "section" | "article";
}) {
  return (
    <Tag className={`reveal ${className}`} style={{ "--reveal-delay": `${delay}ms` } as CSSProperties}>
      {children}
    </Tag>
  );
}

export function Section({
  id,
  index,
  label,
  children,
  className = "",
  tone = "paper",
}: {
  id: string;
  index: string;
  label: string;
  children: ReactNode;
  className?: string;
  tone?: "paper" | "tint" | "ink";
}) {
  const tones = {
    paper: "bg-paper text-ink-2",
    tint: "bg-paper-2 text-ink-2",
    ink: "bg-ink text-mist-200",
  } as const;

  return (
    <section
      id={id}
      aria-labelledby={`${id}-title`}
      className={`scroll-mt-24 border-t border-hair ${tones[tone]} ${className}`}
    >
      <div className="mx-auto w-full max-w-[1240px] px-5 sm:px-8">
        <div className="flex items-baseline gap-4 pt-14 sm:pt-20">
          <span className="font-mono text-[11px] font-medium tabular-nums text-brand-800">{index}</span>
          <span
            className={`font-mono text-[11px] uppercase tracking-[0.24em] ${
              tone === "ink" ? "text-brand-500" : "text-ink-3"
            }`}
          >
            {label}
          </span>
        </div>
        {children}
      </div>
    </section>
  );
}

export function SectionTitle({
  id,
  children,
  tone = "paper",
  className = "",
}: {
  id: string;
  children: ReactNode;
  tone?: "paper" | "tint" | "ink";
  className?: string;
}) {
  return (
    <h2
      id={id}
      className={`text-balance text-[clamp(1.9rem,5.2vw,3.4rem)] font-semibold leading-[1.04] ${
        tone === "ink" ? "text-white" : "text-ink"
      } ${className}`}
    >
      {children}
    </h2>
  );
}

export function Lede({
  children,
  tone = "paper",
  className = "",
}: {
  children: ReactNode;
  tone?: "paper" | "tint" | "ink";
  className?: string;
}) {
  return (
    <p
      className={`text-pretty text-[15px] leading-relaxed sm:text-base ${
        tone === "ink" ? "text-mist-300" : "text-ink-2"
      } ${className}`}
    >
      {children}
    </p>
  );
}

type ActionTone = "solid" | "outline" | "onInk";

const ACTION: Record<ActionTone, string> = {
  solid:
    "bg-ink text-white hover:bg-brand-800 focus-visible:outline-brand-800 border border-ink hover:border-brand-800",
  outline:
    "border border-hair-2 bg-paper text-ink hover:border-ink focus-visible:outline-ink",
  onInk: "border border-white/25 bg-transparent text-white hover:border-brand-500 hover:text-brand-500 focus-visible:outline-brand-500",
};

const ACTION_SIZE = {
  sm: "h-9 px-4 text-[11px]",
  md: "h-11 px-5 text-xs",
  lg: "h-13 px-7 text-[13px]",
} as const;

function actionClass(tone: ActionTone, size: keyof typeof ACTION_SIZE, className: string) {
  return [
    "group inline-flex items-center justify-center gap-2.5 font-semibold uppercase tracking-[0.14em]",
    "transition-colors duration-200 outline-offset-4 focus-visible:outline-2",
    "disabled:pointer-events-none disabled:opacity-50",
    ACTION[tone],
    ACTION_SIZE[size],
    className,
  ].join(" ");
}

export function ActionLink({
  href,
  children,
  tone = "solid",
  size = "md",
  className = "",
}: {
  href: string;
  children: ReactNode;
  tone?: ActionTone;
  size?: keyof typeof ACTION_SIZE;
  className?: string;
}) {
  return (
    <a href={href} className={actionClass(tone, size, className)}>
      {children}
    </a>
  );
}

export function ActionButton({
  children,
  tone = "solid",
  size = "md",
  type = "button",
  disabled = false,
  className = "",
}: {
  children: ReactNode;
  tone?: ActionTone;
  size?: keyof typeof ACTION_SIZE;
  type?: "button" | "submit";
  disabled?: boolean;
  className?: string;
}) {
  return (
    <button type={type} disabled={disabled} className={actionClass(tone, size, className)}>
      {children}
    </button>
  );
}

/** Small uppercase mono label used for metrics and technical metadata. */
export function MicroLabel({
  children,
  tone = "paper",
  className = "",
}: {
  children: ReactNode;
  tone?: "paper" | "tint" | "ink";
  className?: string;
}) {
  return (
    <span
      className={`font-mono text-[10px] uppercase tracking-[0.2em] ${
        tone === "ink" ? "text-mist-400" : "text-ink-3"
      } ${className}`}
    >
      {children}
    </span>
  );
}
