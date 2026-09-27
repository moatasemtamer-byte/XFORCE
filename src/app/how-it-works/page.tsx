import type { Metadata } from "next";
import { MarketingShell, PageHeader } from "@/components/marketing-shell";
import { Badge, ButtonLink, Panel, PanelTitle } from "@/components/ui";

export const metadata: Metadata = {
  title: "How it works",
  description: "From uploading a ladder diagram to downloading the MSW deliverable, step by step.",
};

const STAGES = [
  {
    tag: "Stage 1",
    title: "Upload",
    body: "The browser posts a multipart request straight to the API route. The server checks the extension, enforces your plan's maximum file size, and confirms you have conversions left this month. Nothing is written to job history until all three pass.",
    detail: ["5 MB on Free, up to 512 MB on Enterprise", "15 accepted extensions", "5 attempts per IP per 15 minutes on registration"],
  },
  {
    tag: "Stage 2",
    title: "Record",
    body: "A job is created with a unique identifier and marked as processing. The source file is written under your account folder, and the options you chose are stored verbatim so the engine call is reproducible.",
    detail: ["Job id is the audit anchor", "Options snapshot stored as JSON", "Retention clock starts here"],
  },
  {
    tag: "Stage 3",
    title: "Convert",
    body: "The platform calls your engine at MSW_ENGINE_URL/convert with the file, the job id, the options and the license key. The response is a list of artifacts plus any warnings the engine raised.",
    detail: ["120 second default timeout", "Non-2xx responses become readable errors", "Engine version recorded on the job"],
  },
  {
    tag: "Stage 4",
    title: "Store and hand back",
    body: "Artifacts are written to the job's output folder, the job flips to completed, and the license's last-used timestamp is updated. Downloads are served from that folder and nowhere else.",
    detail: ["Path traversal blocked on download", "Per-file download links", "Warnings shown on the job"],
  },
  {
    tag: "Stage 5",
    title: "Expire",
    body: "When a job passes your plan's retention window it is deleted — database rows and files together. You never have to clean up old drawings, and old drawings never linger on someone else's machine.",
    detail: ["7 days Free, 730 days Enterprise", "Runs automatically on next upload", "No cron required"],
  },
];

export default function HowItWorksPage() {
  return (
    <MarketingShell>
      <PageHeader
        eyebrow="How it works"
        title="Five stages, no surprises"
        description="This is the whole path of a drawing through the platform. Nothing here is theoretical — it is the implemented behaviour."
      />

      <section className="mx-auto max-w-4xl px-5 py-16">
        <div className="space-y-6">
          {STAGES.map((stage) => (
            <Panel key={stage.tag} className="p-6">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <h2 className="text-base font-semibold">{stage.title}</h2>
                <Badge tone="info">{stage.tag}</Badge>
              </div>
              <p className="mt-3 text-sm leading-relaxed text-mist-400">{stage.body}</p>
              <ul className="mt-4 flex flex-wrap gap-2">
                {stage.detail.map((item) => (
                  <li key={item}>
                    <Badge>{item}</Badge>
                  </li>
                ))}
              </ul>
            </Panel>
          ))}
        </div>
      </section>

      <section className="border-t border-ink-800 bg-ink-900/30">
        <div className="mx-auto max-w-3xl px-5 py-16">
          <Panel>
            <PanelTitle
              title="The engine contract, in one paragraph"
              description="Everything the platform needs from your engine."
            />
            <div className="space-y-3 px-5 py-5 text-sm leading-relaxed text-mist-400">
              <p>
                The platform sends <code className="font-mono text-signal-300">multipart/form-data</code> to{" "}
                <code className="font-mono text-signal-300">POST {`{MSW_ENGINE_URL}`}/convert</code> with the
                fields <code className="font-mono text-mist-200">file</code>,{" "}
                <code className="font-mono text-mist-200">jobId</code>,{" "}
                <code className="font-mono text-mist-200">options</code> and{" "}
                <code className="font-mono text-mist-200">licenseKey</code>. An optional{" "}
                <code className="font-mono text-mist-200">Authorization: Bearer</code> header carries
                <code className="font-mono text-mist-200"> MSW_ENGINE_TOKEN</code>.
              </p>
              <p>
                Reply with <code className="font-mono text-mist-200">application/json</code> and an{" "}
                <code className="font-mono text-mist-200">artifacts</code> array, where each entry has a{" "}
                <code className="font-mono text-mist-200">name</code>, a{" "}
                <code className="font-mono text-mist-200">mimeType</code>, a{" "}
                <code className="font-mono text-mist-200">kind</code> and either{" "}
                <code className="font-mono text-mist-200">contentBase64</code> or a{" "}
                <code className="font-mono text-mist-200">url</code>. Returning a non-JSON body is also fine — it
                is stored as a single artifact.
              </p>
              <p>
                The platform reads the <code className="font-mono text-mist-200">warnings</code> array onto the job
                so nothing is lost, and records <code className="font-mono text-mist-200">engineVersion</code> for
                traceability. Swap engines by changing one environment variable.
              </p>
            </div>
          </Panel>

          <div className="mt-8 text-center">
            <ButtonLink href="/register" size="lg">
              Try it on your own drawing
            </ButtonLink>
          </div>
        </div>
      </section>
    </MarketingShell>
  );
}
