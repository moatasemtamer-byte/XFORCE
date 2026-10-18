import { PipelineFigure } from "./ladder";
import { ActionLink, MicroLabel } from "./primitives";

const PROOF = [
  { label: "Sources", value: "Text PDF · ASCII" },
  { label: "Outputs", value: "6 per run" },
  { label: "Analysis", value: "Deterministic" },
] as const;

export function Hero() {
  return (
    <section aria-labelledby="hero-title" className="relative overflow-hidden border-b border-hair bg-paper">
      <div aria-hidden="true" className="grid-fine pointer-events-none absolute inset-0 opacity-60" />
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-brand-800 to-transparent"
      />

      <div className="relative mx-auto w-full max-w-[1240px] px-5 pb-16 pt-14 sm:px-8 sm:pb-24 sm:pt-20">
        <div className="grid items-start gap-14 lg:grid-cols-[1fr_1.05fr] lg:gap-16">
          {/* copy */}
          <div>
            <p className="rise font-mono text-[10.5px] uppercase tracking-[0.26em] text-brand-800">
              Industrial logic, reconstructed
            </p>

            <h1
              id="hero-title"
              className="rise mt-6 text-balance text-[clamp(2.3rem,6vw,4rem)] font-semibold leading-[1.03] tracking-[-0.03em] text-ink"
              style={{ animationDelay: "80ms" }}
            >
              From ladder logic to structured engineering data.
            </h1>

            <p
              className="rise mt-6 max-w-xl text-pretty text-base leading-relaxed text-ink-2 sm:text-lg"
              style={{ animationDelay: "160ms" }}
            >
              XFORCEMSW reads supported PLC ladder-logic documents and text-based logic exports, reconstructs the logic
              behind them, generates Boolean equations, traces signal dependencies, and produces engineering-ready
              reports and structured outputs.
            </p>

            <p
              className="rise mt-5 max-w-xl font-mono text-[11px] uppercase tracking-[0.18em] text-ink-3"
              style={{ animationDelay: "200ms" }}
            >
              Document <span aria-hidden="true" className="text-brand-800">&rarr;</span> logic{" "}
              <span aria-hidden="true" className="text-brand-800">&rarr;</span> analysis{" "}
              <span aria-hidden="true" className="text-brand-800">&rarr;</span> engineering data
            </p>

            <div className="rise mt-9 flex flex-wrap items-center gap-3" style={{ animationDelay: "240ms" }}>
              <ActionLink href="/enter-license" size="lg">
                Analyze your logic
              </ActionLink>
              <ActionLink href="/pricing" tone="outline" size="lg">
                Get a license
              </ActionLink>
              <ActionLink href="/contact" tone="outline" size="lg">
                Contact us
              </ActionLink>
            </div>

            <dl
              className="rise mt-12 grid max-w-lg grid-cols-3 gap-px border border-hair bg-hair"
              style={{ animationDelay: "320ms" }}
            >
              {PROOF.map((item) => (
                <div key={item.label} className="bg-paper px-3.5 py-4">
                  <dt className="font-mono text-[9.5px] uppercase tracking-[0.16em] text-ink-3">{item.label}</dt>
                  <dd className="mt-1.5 text-[13px] font-semibold leading-tight text-ink">{item.value}</dd>
                </div>
              ))}
            </dl>
          </div>

          {/* diagram */}
          <div>
            <MicroLabel>Analysis pipeline</MicroLabel>
            <div className="mt-4 w-full">
              <PipelineFigure />
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
