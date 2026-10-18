import type { ReactNode } from "react";

/**
 * Ladder-logic visual language.
 *
 * The diagram is built from the symbols the conversion engine actually reads
 * (series contacts, normally-closed contacts, a branch, a timer, a counter and
 * coils), and the green signal travels the rungs left to right, energising each
 * device as it arrives.
 *
 * Every animated path is authored with pathLength={1}, so one dash length works
 * at any rendered size. Timing is derived from a single cycle so the signal and
 * the device it reaches always agree.
 */
const CYCLE = 4.2;
/** Fraction of the cycle the signal spends crossing one rung. */
const SWEEP = 0.46;
/** Length of the lit window for a device, as a fraction of the cycle. */
const FLASH = 0.1;

/** Seconds after the cycle starts at which the signal reaches `fraction` of the rung. */
function arrival(rung: number, fraction: number) {
  return rung * 0.34 + fraction * CYCLE * SWEEP;
}

const delay = (seconds: number) => ({ animationDelay: `${Math.max(0, seconds - FLASH * CYCLE).toFixed(2)}s` });

function NoContact({ x, y, label, rung, fraction }: { x: number; y: number; label: string; rung: number; fraction: number }) {
  return (
    <g>
      <rect x={x - 26} y={y - 19} width={52} height={38} fill="var(--color-paper)" />
      <g
        className="charge"
        style={delay(arrival(rung, fraction))}
        stroke="var(--color-ink)"
        strokeWidth={2.4}
        strokeLinecap="round"
        fill="none"
      >
        <line x1={x - 9} y1={y - 13} x2={x - 9} y2={y + 13} />
        <line x1={x + 9} y1={y - 13} x2={x + 9} y2={y + 13} />
      </g>
      <text className="diagram-label" x={x} y={y - 26} textAnchor="middle" fill="var(--color-ink-3)" fontSize={13} letterSpacing="0.08em">
        {label}
      </text>
    </g>
  );
}

function NcContact({ x, y, label, rung, fraction }: { x: number; y: number; label: string; rung: number; fraction: number }) {
  return (
    <g>
      <rect x={x - 26} y={y - 19} width={52} height={38} fill="var(--color-paper)" />
      <g
        className="charge"
        style={delay(arrival(rung, fraction))}
        stroke="var(--color-ink)"
        strokeWidth={2.4}
        strokeLinecap="round"
        fill="none"
      >
        <line x1={x - 9} y1={y - 13} x2={x - 9} y2={y + 13} />
        <line x1={x + 9} y1={y - 13} x2={x + 9} y2={y + 13} />
        <line x1={x - 14} y1={y + 15} x2={x + 14} y2={y - 15} strokeWidth={1.8} />
      </g>
      <text className="diagram-label" x={x} y={y - 26} textAnchor="middle" fill="var(--color-ink-3)" fontSize={13} letterSpacing="0.08em">
        {label}
      </text>
    </g>
  );
}

function Coil({ x, y, label, rung, fraction }: { x: number; y: number; label: string; rung: number; fraction: number }) {
  return (
    <g>
      <rect x={x - 34} y={y - 22} width={68} height={44} fill="var(--color-paper)" />
      <g
        className="charge"
        style={delay(arrival(rung, fraction))}
        stroke="var(--color-ink)"
        strokeWidth={2.2}
        fill="none"
        strokeLinecap="round"
      >
        <path d={`M ${x - 30} ${y - 14} A 30 30 0 0 0 ${x - 30} ${y + 14}`} />
        <circle cx={x} cy={y} r={13} />
        <path d={`M ${x + 30} ${y - 14} A 30 30 0 0 1 ${x + 30} ${y + 14}`} />
      </g>
      <text x={x} y={y + 4.5} textAnchor="middle" fill="var(--color-ink)" fontSize={12} fontWeight={600} letterSpacing="0.04em">
        {label}
      </text>
    </g>
  );
}

function Block({
  x,
  y,
  tag,
  value,
  rung,
  fraction,
}: {
  x: number;
  y: number;
  tag: string;
  value: string;
  rung: number;
  fraction: number;
}) {
  const w = 62;
  const h = 34;
  return (
    <g>
      <rect x={x - w / 2 - 4} y={y - h / 2 - 4} width={w + 8} height={h + 8} fill="var(--color-paper)" />
      <g className="charge" style={delay(arrival(rung, fraction))}>
        <rect
          x={x - w / 2}
          y={y - h / 2}
          width={w}
          height={h}
          fill="none"
          stroke="var(--color-ink)"
          strokeWidth={2}
        />
        <line x1={x - w / 2} y1={y - h / 2 + 11} x2={x + w / 2} y2={y - h / 2 + 11} stroke="var(--color-ink)" strokeWidth={1} />
        <text x={x} y={y - h / 2 + 9} textAnchor="middle" fill="var(--color-ink)" fontSize={10} fontWeight={700} letterSpacing="0.1em">
          {tag}
        </text>
        <text x={x} y={y + h / 2 - 6} textAnchor="middle" fill="var(--color-ink-3)" fontSize={11} letterSpacing="0.06em">
          {value}
        </text>
      </g>
    </g>
  );
}

function Rung({
  y,
  index,
  from = 60,
  to = 700,
  children,
  dashed = false,
}: {
  y: number;
  index: number;
  from?: number;
  to?: number;
  children?: ReactNode;
  dashed?: boolean;
}) {
  return (
    <g>
      <line
        x1={from}
        y1={y}
        x2={to}
        y2={y}
        stroke="var(--color-hair-2)"
        strokeWidth={1.5}
        strokeDasharray={dashed ? "5 6" : undefined}
      />
      <path
        className="signal"
        pathLength={1}
        style={{ animationDelay: `${(index * 0.34).toFixed(2)}s` }}
        d={`M ${from} ${y} L ${to} ${y}`}
      />
      <text x={from - 14} y={y + 4} textAnchor="end" fill="var(--color-ink-4)" fontSize={11} letterSpacing="0.1em">
        {String(index + 1).padStart(2, "0")}
      </text>
      {children}
    </g>
  );
}

/** Hero diagram: rails, four rungs of real ladder symbols, live signal. */
export function LadderFigure({ className = "" }: { className?: string }) {
  return (
    <figure className={`ticks relative border border-hair bg-paper p-4 sm:p-6 ${className}`}>
      <svg
        viewBox="0 0 760 470"
        className="h-auto w-full"
        role="img"
        aria-label="Ladder logic diagram: three series contacts drive a motor coil, a normally closed stop contact with a timer drives an alarm coil, and a branched input drives a coil. A green signal travels each rung from left to right."
      >
        {/* power rails */}
        <g stroke="var(--color-hair-2)" strokeWidth={3} strokeLinecap="round">
          <line x1={60} y1={30} x2={60} y2={436} />
          <line x1={700} y1={30} x2={700} y2={436} />
        </g>
        <text className="diagram-label-sm diagram-hide-sm" x={60} y={20} textAnchor="middle" fill="var(--color-ink-4)" fontSize={10} letterSpacing="0.18em">
          LEFT RAIL
        </text>
        <text className="diagram-label-sm diagram-hide-sm" x={700} y={20} textAnchor="middle" fill="var(--color-ink-4)" fontSize={10} letterSpacing="0.18em">
          RIGHT RAIL
        </text>

        {/* rung 01 — three series contacts into a coil */}
        <Rung y={100} index={0}>
          <NoContact x={200} y={100} label="ESD1" rung={0} fraction={0.22} />
          <NoContact x={300} y={100} label="ESD2" rung={0} fraction={0.4} />
          <NoContact x={400} y={100} label="ESD3" rung={0} fraction={0.58} />
          <Coil x={560} y={100} label="M1" rung={0} fraction={0.82} />
        </Rung>

        {/* rung 02 — normally closed contact, timer, alarm coil */}
        <Rung y={196} index={1}>
          <NcContact x={200} y={196} label="STOP_BTN" rung={1} fraction={0.22} />
          <Block x={320} y={196} tag="TMR" value="01" rung={1} fraction={0.44} />
          <Block x={420} y={196} tag="CNT" value="02" rung={1} fraction={0.6} />
          <Coil x={560} y={196} label="ALARM" rung={1} fraction={0.82} />
        </Rung>

        {/* rung 03 — branched input into a coil */}
        <Rung y={292} index={2}>
          <NoContact x={200} y={292} label="SYSMP_BAD" rung={2} fraction={0.22} />
          <g stroke="var(--color-hair-2)" strokeWidth={1.5} fill="none">
            <path d="M 320 292 L 350 292 L 350 344 L 320 344" />
            <path d="M 320 318 L 350 318" />
          </g>
          <circle className="charge" style={delay(arrival(2, 0.5))} cx={350} cy={292} r={3.5} fill="var(--color-brand-700)" />
          <Coil x={470} y={292} label="NSS9801" rung={2} fraction={0.66} />
        </Rung>

        {/* rung 04 — hand-off into the engine */}
        <Rung y={390} index={3} dashed>
          <circle cx={140} cy={390} r={4} fill="none" stroke="var(--color-ink-4)" strokeWidth={1.5} />
          <circle className="charge" style={delay(arrival(3, 0.9))} cx={640} cy={390} r={4} fill="var(--color-brand-700)" />
        </Rung>

        <text className="diagram-label-sm diagram-hide-sm" x={140} y={414} fill="var(--color-ink-4)" fontSize={10} letterSpacing="0.18em">
          HAND-OFF TO XFORCE
        </text>
      </svg>

      <figcaption className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-hair pt-3">
        <span className="font-mono text-[10px] uppercase tracking-[0.18em] text-ink-3">
          Contacts · coils · timers · counters · branch
        </span>
        <span className="flex items-center gap-2 font-mono text-[10px] uppercase tracking-[0.18em] text-ink-3">
          <span aria-hidden="true" className="relative flex h-2 w-2">
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-brand-700 opacity-70" />
            <span className="relative inline-flex h-2 w-2 rounded-full bg-brand-700" />
          </span>
          Signal path
        </span>
      </figcaption>
    </figure>
  );
}

/** Compact ladder used inside the engine console. */
export function LadderStrip({ className = "" }: { className?: string }) {
  return (
    <svg viewBox="0 0 360 120" className={`h-auto w-full ${className}`} aria-hidden="true">
      <g stroke="var(--color-ink-4)" strokeWidth={2.5} strokeLinecap="round">
        <line x1={12} y1={8} x2={12} y2={112} />
        <line x1={348} y1={8} x2={348} y2={112} />
      </g>
      {[34, 64, 94].map((y, i) => (
        <g key={y}>
          <line x1={12} y1={y} x2={348} y2={y} stroke="var(--color-hair-2)" strokeWidth={1.5} />
          <path className="signal signal-thin" pathLength={1} style={{ animationDelay: `${i * 0.5}s` }} d={`M 12 ${y} L 348 ${y}`} />
        </g>
      ))}
      <g fill="var(--color-paper)">
        <rect x={96} y={22} width={28} height={24} />
        <rect x={150} y={22} width={28} height={24} />
        <rect x={248} y={22} width={34} height={24} />
        <rect x={96} y={52} width={28} height={24} />
        <rect x={176} y={52} width={48} height={24} />
        <rect x={248} y={52} width={34} height={24} />
        <rect x={120} y={82} width={28} height={24} />
        <rect x={248} y={82} width={34} height={24} />
      </g>
      <g stroke="var(--color-ink)" strokeWidth={2} fill="none">
        <g className="charge" style={delay(0)}>
          <line x1={106} y1={28} x2={106} y2={40} />
          <line x1={118} y1={28} x2={118} y2={40} />
          <circle cx={265} cy={34} r={9} />
        </g>
        <g className="charge" style={delay(0.5)}>
          <line x1={106} y1={58} x2={106} y2={70} />
          <line x1={118} y1={58} x2={118} y2={70} />
          <line x1={102} y1={72} x2={122} y2={56} strokeWidth={1.4} />
          <rect x={182} y={57} width={36} height={14} />
          <circle cx={265} cy={64} r={9} />
        </g>
        <g className="charge" style={delay(1)}>
          <line x1={130} y1={88} x2={130} y2={100} />
          <line x1={142} y1={88} x2={142} y2={100} />
          <circle cx={265} cy={94} r={9} />
        </g>
      </g>
    </svg>
  );
}

const STAGES = [
  { key: "source", label: "Source", note: "PDF / ASCII" },
  { key: "parse", label: "Parse", note: "Dialect rules" },
  { key: "model", label: "Logic model", note: "Rungs · signals" },
  { key: "analyse", label: "Analyse", note: "Bool · xref" },
  { key: "outputs", label: "Outputs", note: "Many formats" },
] as const;

/** SOURCE -> PARSE -> LOGIC MODEL -> ANALYSE -> OUTPUTS, with a marching signal. */
export function FlowStrip({ tone = "paper" }: { tone?: "paper" | "tint" | "ink" }) {
  const dark = tone === "ink";
  return (
    <ol className="flex flex-col items-stretch gap-0 sm:flex-row sm:items-center">
      {STAGES.map((stage, i) => (
        <li key={stage.key} className="flex flex-col items-center gap-0 sm:flex-row sm:flex-1">
          <div
            className={`flex w-full flex-col items-center gap-1 border px-3 py-4 text-center sm:w-[9.5rem] ${
              dark ? "border-white/15 bg-white/[0.03]" : "border-hair bg-paper"
            }`}
          >
            <span className="font-mono text-[10px] tabular-nums text-brand-800 dark:text-brand-500">
              {String(i + 1).padStart(2, "0")}
            </span>
            <span className={`text-[13px] font-semibold uppercase tracking-[0.1em] ${dark ? "text-white" : "text-ink"}`}>
              {stage.label}
            </span>
            <span className={`font-mono text-[10px] ${dark ? "text-mist-400" : "text-ink-3"}`}>{stage.note}</span>
          </div>
          {i < STAGES.length - 1 ? (
            <span
              aria-hidden="true"
              className="relative flex h-10 w-full items-center justify-center sm:h-px sm:w-6 sm:shrink-0 sm:flex-1"
            >
              <span className={`absolute h-px w-full sm:h-full sm:w-px ${dark ? "bg-white/15" : "bg-hair"}`} />
              <span className={`march-line-x absolute h-px w-full sm:hidden ${dark ? "opacity-40" : ""}`} />
              <span className={`march-line-y absolute h-full w-px sm:hidden ${dark ? "opacity-40" : ""}`} />
            </span>
          ) : null}
        </li>
      ))}
    </ol>
  );
}

/* ------------------------------------------------------------------ *
 * Pipeline figure — the hero diagram
 * ------------------------------------------------------------------ */

const OUTPUTS = [
  { key: "BOOL", label: "Boolean", note: "equations" },
  { key: "XREF", label: "Cross-ref", note: "signals" },
  { key: "RPT", label: "Report", note: "findings" },
  { key: "XLS", label: "Workbook", note: "tables" },
  { key: "HND", label: "Handoff", note: "TRISTATION" },
] as const;

/** Output card centres in figure space. */
const OUT_CX = 578;
const OUT_CY = [64, 148, 232, 316, 400] as const;
const OUT_W = 214;
const OUT_H = 56;

/**
 * The transformation the product actually performs, drawn as one figure:
 * a ladder document is parsed, becomes a logic model, and fans out into
 * several engineering outputs. The signal animates along the real paths —
 * document -> engine -> each output — rather than as decoration.
 */
export function PipelineFigure({ className = "" }: { className?: string }) {
  return (
    <figure className={`ticks relative border border-hair bg-paper p-4 sm:p-6 ${className}`}>
      <svg
        viewBox="0 0 800 464"
        className="h-auto w-full"
        role="img"
        aria-label="Pipeline diagram: a ladder logic document with contacts, a branch, a coil, a timer and a counter is parsed by the XForce engine into a logic model, which produces five outputs: Boolean equations, cross-reference data, an engineering report, an Excel workbook, and a TRISTATION logic handoff."
      >
        <defs>
          <clipPath id="doc-clip">
            <rect x="0" y="0" width="252" height="400" />
          </clipPath>
        </defs>

        {/* ---------- source document ---------- */}
        <g>
          <rect x="12" y="32" width="252" height="400" fill="var(--color-paper-2)" stroke="var(--color-hair-2)" strokeWidth={1.5} />
          <g clipPath="url(#doc-clip)">
            <g transform="translate(12 32)">
              <rect x="18" y="16" width="120" height="6" fill="var(--color-hair-2)" />
              <rect x="18" y="28" width="86" height="5" fill="var(--color-hair)" />

              {/* rungs: a real small ladder */}
              <g transform="translate(0 56)">
                <line x1="24" y1="0" x2="24" y2="150" stroke="var(--color-hair-2)" strokeWidth={2} />
                <line x1="228" y1="0" x2="228" y2="150" stroke="var(--color-hair-2)" strokeWidth={2} />

                {/* rung 1 */}
                <line x1="24" y1="22" x2="228" y2="22" stroke="var(--color-hair-2)" strokeWidth={1.2} />
                <g className="charge" style={delay(0.05)} stroke="var(--color-ink-3)" strokeWidth={1.6} fill="none">
                  <line x1="62" y1="15" x2="62" y2="29" />
                  <line x1="76" y1="15" x2="76" y2="29" />
                  <line x1="112" y1="15" x2="112" y2="29" />
                  <line x1="126" y1="15" x2="126" y2="29" />
                  <circle cx="186" cy="22" r="9" />
                </g>
                <text x="186" y="25.5" textAnchor="middle" fill="var(--color-ink-3)" fontSize={8} fontWeight={700}>
                  M
                </text>
                <text x="34" y="12" fill="var(--color-ink-4)" fontSize={8} letterSpacing="0.08em">
                  START
                </text>
                <text x="150" y="12" fill="var(--color-ink-4)" fontSize={8} letterSpacing="0.08em">
                  MOTOR_RUN
                </text>

                {/* rung 2 — branch + timer */}
                <line x1="24" y1="72" x2="228" y2="72" stroke="var(--color-hair-2)" strokeWidth={1.2} />
                <path d="M 118 72 L 140 72 L 140 100 L 118 100" fill="none" stroke="var(--color-hair-2)" strokeWidth={1.2} />
                <line x1="118" y1="86" x2="140" y2="86" stroke="var(--color-hair-2)" strokeWidth={1.2} />
                <g className="charge" style={delay(0.5)} stroke="var(--color-ink-3)" strokeWidth={1.6} fill="none">
                  <line x1="56" y1="65" x2="56" y2="79" />
                  <line x1="70" y1="65" x2="70" y2="79" />
                  <line x1="48" y1="83" x2="78" y2="61" strokeWidth={1.2} />
                  <rect x="160" y="60" width="44" height="24" />
                </g>
                <line x1="160" y1="68" x2="204" y2="68" stroke="var(--color-ink-3)" strokeWidth={1} />
                <text x="182" y="66" textAnchor="middle" fill="var(--color-ink-3)" fontSize={7.5} fontWeight={700} letterSpacing="0.06em">
                  TMR
                </text>
                <text x="34" y="62" fill="var(--color-ink-4)" fontSize={8} letterSpacing="0.08em">
                  STOP_BTN
                </text>

                {/* rung 3 — counter + coil */}
                <line x1="24" y1="126" x2="228" y2="126" stroke="var(--color-hair-2)" strokeWidth={1.2} />
                <g className="charge" style={delay(0.95)} stroke="var(--color-ink-3)" strokeWidth={1.6} fill="none">
                  <line x1="62" y1="119" x2="62" y2="133" />
                  <line x1="76" y1="119" x2="76" y2="133" />
                  <rect x="150" y="114" width="44" height="24" />
                  <circle cx="204" cy="126" r="9" />
                </g>
                <line x1="150" y1="122" x2="194" y2="122" stroke="var(--color-ink-3)" strokeWidth={1} />
                <text x="172" y="120" textAnchor="middle" fill="var(--color-ink-3)" fontSize={7.5} fontWeight={700} letterSpacing="0.06em">
                  CNT
                </text>
                <text x="34" y="116" fill="var(--color-ink-4)" fontSize={8} letterSpacing="0.08em">
                  SYSMP_BAD
                </text>

                {/* scan pass over the page */}
                <rect className="scan-pass" x="0" y="0" width="252" height="46" fill="var(--color-brand-700)" opacity={0.1} />
              </g>
            </g>
          </g>
          <text x="12" y="20" fill="var(--color-ink-4)" fontSize={9} letterSpacing="0.2em" fontFamily="var(--font-geist-mono)">
            LADDER DOCUMENT
          </text>
        </g>

        {/* ---------- document -> engine ---------- */}
        <g>
          <line x1="264" y1="232" x2="316" y2="232" stroke="var(--color-hair-2)" strokeWidth={1.5} />
          <path className="signal" pathLength={1} d="M 264 232 L 316 232" />
          <polygon points="316,227 326,232 316,237" fill="var(--color-brand-700)" />
        </g>
        <text x="290" y="222" textAnchor="middle" fill="var(--color-ink-4)" fontSize={9} letterSpacing="0.14em">
          PARSE
        </text>

        {/* ---------- engine ---------- */}
        <g>
          <rect x="330" y="176" width="124" height="112" fill="var(--color-ink)" />
          <rect x="338" y="184" width="108" height="96" fill="none" stroke="var(--color-brand-700)" strokeWidth={1.5} />
          <text x="392" y="222" textAnchor="middle" fill="#ffffff" fontSize={15} fontWeight={700} letterSpacing="0.14em">
            XFORCE
          </text>
          <text x="392" y="242" textAnchor="middle" fill="var(--color-brand-500)" fontSize={12} fontWeight={700} letterSpacing="0.2em">
            ENGINE
          </text>
          <line x1="356" y1="256" x2="428" y2="256" stroke="var(--color-brand-700)" strokeWidth={1} />
          <text x="392" y="270" textAnchor="middle" fill="var(--color-mist-400)" fontSize={8.5} letterSpacing="0.12em">
            LOGIC MODEL
          </text>
        </g>

        {/* ---------- engine -> outputs ---------- */}
        <g>
          <line x1="454" y1="232" x2="474" y2="232" stroke="var(--color-hair-2)" strokeWidth={1.5} />
          <path className="signal" pathLength={1} d="M 454 232 L 474 232" />
          <line x1="474" y1={OUT_CY[0]} x2="474" y2={OUT_CY[4]} stroke="var(--color-hair-2)" strokeWidth={1.5} />
          {OUT_CY.map((y, i) => (
            <g key={y}>
              <line x1="474" y1={y} x2="524" y2={y} stroke="var(--color-hair-2)" strokeWidth={1.5} />
              <path className="signal" pathLength={1} style={{ animationDelay: `${(i * 0.3).toFixed(2)}s` }} d={`M 474 ${y} L 524 ${y}`} />
              <polygon points={`524,${y - 5} 534,${y} 524,${y + 5}`} fill="var(--color-brand-700)" />
            </g>
          ))}
        </g>
        <text x="499" y="222" textAnchor="middle" fill="var(--color-ink-4)" fontSize={9} letterSpacing="0.14em">
          EXPORT
        </text>

        {/* ---------- outputs ---------- */}
        {OUTPUTS.map((out, i) => {
          const y = OUT_CY[i] - OUT_H / 2;
          return (
            <g key={out.key} className="charge" style={delay(0.4 + i * 0.3)}>
              <rect x={OUT_CX} y={y} width={OUT_W} height={OUT_H} fill="var(--color-paper)" stroke="var(--color-hair-2)" strokeWidth={1.5} />
              <rect x={OUT_CX} y={y} width={OUT_W} height={OUT_H} fill="none" stroke="var(--color-brand-800)" strokeWidth={1.5} />
              <text x={OUT_CX + 18} y={y + 25} fill="var(--color-ink-4)" fontSize={9} letterSpacing="0.18em" fontFamily="var(--font-geist-mono)">
                {out.key}
              </text>
              <text x={OUT_CX + 18} y={y + 42} fill="var(--color-ink)" fontSize={12.5} fontWeight={600}>
                {out.label}
              </text>
              <text x={OUT_CX + OUT_W - 14} y={y + 42} textAnchor="end" fill="var(--color-ink-3)" fontSize={9.5} letterSpacing="0.1em">
                {out.note}
              </text>
            </g>
          );
        })}
      </svg>

      <figcaption className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-hair pt-3">
        <span className="font-mono text-[10px] uppercase tracking-[0.18em] text-ink-3">
          One source · one analysis · many outputs
        </span>
        <span className="flex items-center gap-2 font-mono text-[10px] uppercase tracking-[0.18em] text-ink-3">
          <span aria-hidden="true" className="relative flex h-2 w-2">
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-brand-700 opacity-70" />
            <span className="relative inline-flex h-2 w-2 rounded-full bg-brand-700" />
          </span>
          Analysis path
        </span>
      </figcaption>
    </figure>
  );
}
