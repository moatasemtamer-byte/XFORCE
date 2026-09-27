const RUNGS = [
  { left: 0, middle: "NO", right: "M1", note: "Start / stop interlock" },
  { left: 1, middle: "SE", right: "M2", note: "Safety relay" },
  { left: 2, middle: "NO", right: "M3", note: "Pump stage 1" },
  { left: 3, middle: "NC", right: "M4", note: "Overload trip" },
];

export function LadderPreview({ className = "" }: { className?: string }) {
  return (
    <svg viewBox="0 0 460 260" className={className} role="img" aria-label="Ladder diagram sample">
      <defs>
        <linearGradient id="ladder-rail" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="var(--color-signal-400)" stopOpacity="0.9" />
          <stop offset="1" stopColor="var(--color-signal-600)" stopOpacity="0.35" />
        </linearGradient>
      </defs>

      <rect x="0" y="0" width="460" height="260" rx="12" fill="var(--color-ink-900)" />
      <rect
        x="0.5"
        y="0.5"
        width="459"
        height="259"
        rx="11.5"
        fill="none"
        stroke="var(--color-ink-700)"
      />

      <text x="18" y="26" fill="var(--color-mist-500)" fontSize="9" fontFamily="monospace" letterSpacing="1.6">
        XFORCE-MSW / SHEET 01
      </text>
      <text x="442" y="26" fill="var(--color-signal-400)" fontSize="9" fontFamily="monospace" textAnchor="end">
        IEC 61131-3
      </text>

      <line x1="34" y1="40" x2="34" y2="232" stroke="url(#ladder-rail)" strokeWidth="2.5" />
      <line x1="426" y1="40" x2="426" y2="232" stroke="url(#ladder-rail)" strokeWidth="2.5" />

      {RUNGS.map((rung, index) => {
        const y = 64 + index * 44;
        return (
          <g key={rung.note}>
            <line x1="34" y1={y} x2="426" y2={y} stroke="var(--color-ink-500)" strokeWidth="1.6" />

            <rect
              x="70"
              y={y - 8}
              width="30"
              height="16"
              rx="2"
              fill="var(--color-ink-900)"
              stroke="var(--color-mist-300)"
              strokeWidth="1.5"
            />
            {rung.middle === "NC" ? (
              <path
                d={`M76 ${y + 4} L 94 ${y - 4}`}
                stroke="var(--color-bad-500)"
                strokeWidth="1.6"
                strokeLinecap="round"
              />
            ) : (
              <line
                x1="74"
                y1={y}
                x2="96"
                y2={y}
                stroke="var(--color-signal-300)"
                strokeWidth="2"
                strokeLinecap="round"
              />
            )}

            <rect
              x="150"
              y={y - 7}
              width="34"
              height="14"
              rx="2"
              fill="var(--color-ink-900)"
              stroke="var(--color-mist-500)"
              strokeWidth="1.3"
            />
            <text
              x="167"
              y={y + 4}
              fill="var(--color-mist-400)"
              fontSize="9"
              fontFamily="monospace"
              textAnchor="middle"
            >
              {rung.right}
            </text>

            <circle
              cx="300"
              cy={y}
              r="11"
              fill="none"
              stroke="var(--color-amberline-400)"
              strokeWidth="1.6"
            />
            <path
              d={`M296 ${y - 5} A 9 9 0 0 1 296 ${y + 5} Z`}
              fill="var(--color-amberline-400)"
              opacity="0.85"
            />

            <text
              x="326"
              y={y + 4}
              fill="var(--color-mist-500)"
              fontSize="9.5"
              fontFamily="monospace"
            >
              {rung.note}
            </text>
          </g>
        );
      })}
    </svg>
  );
}
