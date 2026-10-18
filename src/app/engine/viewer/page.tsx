import { PtxViewer } from "@/components/engine/pt2x-viewer";
import { getEngineStatus } from "@/lib/engine";

export const dynamic = "force-dynamic";

export default async function EngineViewerPage() {
  const { error } = await getEngineStatus();
  if (error) {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-bold tracking-tight text-nx-ink">Project viewer</h1>
        <div className="border-2 border-nx-fail-fg/50 bg-nx-fail-bg px-5 py-4 text-sm font-semibold text-nx-fail-fg">
          {error}
        </div>
      </div>
    );
  }
  return <PtxViewer />;
}
