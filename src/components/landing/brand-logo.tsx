/**
 * Official XFORCEMSW logo.
 *
 * The artwork is used exactly as supplied: the PNG is served unmodified from
 * /brand and is never redrawn, recoloured, filtered or cropped. Only the
 * environment around it animates.
 *
 * Measured properties of the source file (why it is rendered this way):
 *   canvas 1983 x 793 (2.501:1), opaque near-white #FEFEFE background,
 *   artwork inset 86/78 px horizontally and 242/276 px vertically.
 * Because the file has generous built-in padding and no alpha channel, the
 * image is sized by width and placed on a white plate wherever a tinted
 * surface sits behind it.
 */
const LOGO_SRC = "/brand/xforcemsw-logo.png";
const LOGO_RATIO = 1983 / 793;

export type LogoSize = "sm" | "md" | "lg" | "xl";

const WIDTH: Record<LogoSize, number> = {
  sm: 132,
  md: 168,
  lg: 260,
  xl: 420,
};

export function BrandLogo({
  size = "md",
  priority = false,
  className = "",
}: {
  size?: LogoSize;
  priority?: boolean;
  className?: string;
}) {
  const width = WIDTH[size];
  return (
    // eslint-disable-next-line @next/next/no-img-element
    <img
      src={LOGO_SRC}
      alt="XFORCEMSW"
      width={width}
      height={Math.round(width / LOGO_RATIO)}
      loading={priority ? "eager" : "lazy"}
      decoding={priority ? "sync" : "async"}
      fetchPriority={priority ? "high" : "auto"}
      draggable={false}
      className={`block h-auto max-w-full select-none ${className}`}
      style={{ width, aspectRatio: String(LOGO_RATIO) }}
    />
  );
}

/**
 * The logo with a restrained "system powering up" treatment: signal lines
 * terminate at the artwork, a scan bar passes behind it, and a few nodes
 * connect around it. The logo itself only fades and lifts into place.
 */
export function BrandLogoStage({
  size = "xl",
  className = "",
}: {
  size?: LogoSize;
  className?: string;
}) {
  const width = WIDTH[size];
  return (
    <div className={`relative isolate flex justify-center ${className}`}>
      <span
        aria-hidden="true"
        className="bloom pointer-events-none absolute left-1/2 top-1/2 -z-10 h-[135%] w-[135%] -translate-x-1/2 -translate-y-1/2"
      />
      <LogoSignalStage width={width} />
      <div className="scan pointer-events-none absolute inset-0 -z-10 overflow-hidden" aria-hidden="true" />
      <div className="rise" style={{ width }}>
        <BrandLogo size={size} priority />
      </div>
    </div>
  );
}

/** Technical lines that run toward the logo and stop at its edge. */
function LogoSignalStage({ width }: { width: number }) {
  const height = width / LOGO_RATIO;
  return (
    <svg
      aria-hidden="true"
      viewBox={`0 0 ${width} ${height}`}
      className="pointer-events-none absolute inset-0 -z-10 h-full w-full"
      preserveAspectRatio="none"
    >
      <g stroke="var(--color-brand-700)" strokeWidth="1" fill="none" opacity="0.5">
        <path className="signal signal-thin" pathLength={1} d={`M 0 ${height * 0.34} L ${width * 0.3} ${height * 0.34}`} />
        <path
          className="signal signal-thin"
          pathLength={1}
          style={{ animationDelay: "1.2s" }}
          d={`M ${width} ${height * 0.62} L ${width * 0.72} ${height * 0.62}`}
        />
        <path
          className="signal signal-thin"
          pathLength={1}
          style={{ animationDelay: "2.4s" }}
          d={`M ${width * 0.5} 0 L ${width * 0.5} ${height * 0.22}`}
        />
      </g>
      <g fill="var(--color-brand-700)">
        <circle className="charge" cx={width * 0.3} cy={height * 0.34} r="2.5" />
        <circle
          className="charge"
          style={{ animationDelay: "1.2s" }}
          cx={width * 0.72}
          cy={height * 0.62}
          r="2.5"
        />
        <circle
          className="charge"
          style={{ animationDelay: "2.4s" }}
          cx={width * 0.5}
          cy={height * 0.22}
          r="2.5"
        />
      </g>
    </svg>
  );
}
