import { redirect } from "next/navigation";
import { getCurrentUser } from "@/lib/auth";
import { getDashboardContext } from "@/lib/dashboard";
import { PageTitle, UsageBar } from "@/components/dashboard/page-title";
import { ConvertForm } from "@/components/dashboard/convert-form";
import { Badge, Panel, PanelTitle } from "@/components/ui";
import { formatBytes } from "@/lib/util";

export default async function ConvertPage() {
  const user = await getCurrentUser();
  if (!user) redirect("/login");

  const context = await getDashboardContext(user);
  const limitLabel = Number.isFinite(context.plan.monthlyConversions)
    ? String(context.plan.monthlyConversions)
    : "unlimited";

  return (
    <div className="space-y-8">
      <PageTitle
        title="Convert a ladder diagram"
        description="Upload the source drawing and the engine returns the MSW deliverable plus any extra outputs you asked for."
      />

      <div className="grid gap-6 lg:grid-cols-[1.7fr_1fr] lg:items-start">
        <Panel>
          <PanelTitle
            title="Source"
            description="Files stay on the server and are removed automatically once your retention window closes."
          />
          <div className="p-5">
            <ConvertForm maxUploadBytes={context.plan.maxUploadBytes} />
          </div>
        </Panel>

        <div className="space-y-6">
          <Panel className="p-5">
            <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-mist-500">Quota</p>
            <div className="mt-4">
              <UsageBar
                percent={context.usagePercent}
                label={`${context.plan.name} plan`}
                value={`${context.usedThisMonth} / ${limitLabel}`}
              />
            </div>
            <p className="mt-3 text-xs text-mist-500">
              Max {formatBytes(context.plan.maxUploadBytes)} per file · results kept{" "}
              {context.plan.retentionDays} days
            </p>
          </Panel>

          <Panel className="p-5">
            <div className="flex items-center justify-between">
              <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-mist-500">Engine</p>
              <Badge tone={context.engine.ready ? "good" : "warn"}>
                {context.engine.ready ? "Online" : "Stub"}
              </Badge>
            </div>
            <p className="mt-3 text-xs leading-relaxed text-mist-400">{context.engine.detail}</p>
            {!context.engine.ready ? (
              <p className="mt-3 rounded-lg border border-amberline-500/30 bg-amberline-500/10 px-3 py-2.5 text-[11px] leading-relaxed text-amberline-400">
                Uploads are accepted and recorded, but the conversion step will fail with an
                &ldquo;engine not connected&rdquo; error until <code className="font-mono">MSW_ENGINE_URL</code> is
                set. That is expected while the engine is still being integrated.
              </p>
            ) : null}
          </Panel>

          <Panel className="p-5">
            <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-mist-500">
              License used for this job
            </p>
            {context.licenses.length === 0 ? (
              <p className="mt-3 text-xs leading-relaxed text-mist-500">
                You have no license keys yet. Conversions still run on your plan quota, but nothing is
                recorded against a key.
              </p>
            ) : (
              <ul className="mt-3 space-y-2.5">
                {context.licenses.slice(0, 4).map((license) => (
                  <li key={license.id} className="flex items-center justify-between gap-3 text-xs">
                    <span className="truncate font-mono text-mist-300">
                      {license.key.slice(0, 16)}…
                    </span>
                    <span className="shrink-0 text-mist-500">
                      {license.activations}/{license.activation_limit} seats
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Panel>
        </div>
      </div>
    </div>
  );
}
