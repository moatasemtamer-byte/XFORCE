/**
 * Nexus wordmark + Xforce lockup, exactly as supplied in /brand.
 * The Nexus mark is transparent (269×88); the Xforce lockup is the supplied
 * artwork (1900×301) with its own white plate. Neither is redrawn or recoloured.
 */
export function NxBrand() {
  return (
    <div className="flex items-center gap-3">
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        src="/brand/nexus-logo.png"
        alt="Nexus"
        width={269}
        height={88}
        draggable={false}
        className="block h-7 w-auto select-none"
      />
      <span aria-hidden="true" className="h-6 w-px bg-nx-line-strong" />
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        src="/brand/xforce-logo.png"
        alt="Xforce Engineering Solutions"
        width={1900}
        height={301}
        draggable={false}
        className="block h-4 w-auto select-none"
      />
    </div>
  );
}