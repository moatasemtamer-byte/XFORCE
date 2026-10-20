import Link from "next/link";
import { PtxViewer } from "@/components/engine/pt2x-viewer";
import { getEngineStatus } from "@/lib/engine";
import { getCurrentUser } from "@/lib/auth";
import { userHasFeature } from "@/lib/licensing";

export const dynamic = "force-dynamic";

export default async function EngineViewerPage() {
  const user = await getCurrentUser();
  if (!user || !userHasFeature(user.id, "viewer")) {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-bold tracking-tight text-nx-ink">Project viewer</h1>
        <div className="border-2 border-nx-unv-fg/50 bg-nx-unv-bg px-5 py-4 text-sm font-semibold text-nx-unv-fg">
          Your license does not include the project viewer. Contact your administrator to enable it.
        </div>
        <Link href="/dashboard" className="text-xs text-nx-hint underline-offset-4 hover:underline">
          ← Back to dashboard
        </Link>
      </div>
    );
  }

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
