import { EngineConvertForm } from "@/components/engine/convert-form";
import { NxPanel, NxPanelTitle } from "@/components/nx/primitives";
import { getEngineStatus } from "@/lib/engine";

export const dynamic = "force-dynamic";

export default async function EngineConvertPage() {
  const { error } = await getEngineStatus();

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-lg font-semibold text-nx-ink">Convert</h1>
        <p className="mt-1.5 max-w-2xl text-sm text-nx-body">
          Turn ladder logic into the format you need. Feed in a <strong className="font-semibold text-nx-ink">PDF</strong>{" "}
          or <strong className="font-semibold text-nx-ink">TRISTATION listing</strong> and go all the way to a{" "}
          <strong className="font-semibold text-nx-ink">TriStation PT2X</strong> container or a{" "}
          <strong className="font-semibold text-nx-ink">TIA Portal (AP)</strong> package — or stop at the intermediate{" "}
          <strong className="font-semibold text-nx-ink">TriStation XML</strong>. Already have XML? Send it straight to
          PT2X or AP. Already have a <strong className="font-semibold text-nx-ink">Siemens TIA export (AP)</strong>? Bring
          it back to XML or PT2X.
        </p>
      </div>

      {error ? (
        <div className="border border-nx-fail-fg/40 bg-nx-fail-bg px-4 py-3 text-xs text-nx-fail-fg">
          {error}
        </div>
      ) : null}

      <EngineConvertForm />

      <NxPanel>
        <NxPanelTitle title="Workflow" description="Where each piece of evidence comes from." />
        <ol className="space-y-2.5 px-5 py-4 text-xs text-nx-body">
          <li>
            1. Run the conversion here — the engine emits the TIA package and{" "}
            <code className="font-mono text-nx-ink">report.json</code>.
          </li>
          <li>
            2. Import the package in TIA Portal with{" "}
            <code className="font-mono text-nx-ink">import.ps1</code>; it writes{" "}
            <code className="font-mono text-nx-ink">roundtrip.json</code>.
          </li>
          <li>
            3. Tests write <code className="font-mono text-nx-ink">status/tests.trx</code>.
          </li>
          <li>4. The dashboard turns those files into statuses — nothing is assumed.</li>
        </ol>
      </NxPanel>
    </div>
  );
}