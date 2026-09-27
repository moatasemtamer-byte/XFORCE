import type { Metadata } from "next";
import { MarketingShell, PageHeader } from "@/components/marketing-shell";
import { ContactForm } from "@/components/forms/contact-form";
import { Panel, PanelTitle } from "@/components/ui";
import { engineStatus } from "@/lib/converter";

export const metadata: Metadata = {
  title: "Contact",
  description: "Talk to XforceMsw about ladder diagram to MSW conversion, licensing or an enterprise rollout.",
};

const ROUTES = [
  {
    title: "Sales and pilots",
    body: "Volume quotes, on-site training and a scoped pilot on your own drawings.",
    email: "sales@xforcemsw.com",
  },
  {
    title: "Engineering support",
    body: "Conversion problems, engine integration and anything that blocks a job.",
    email: "support@xforcemsw.com",
  },
  {
    title: "Enterprise",
    body: "SSO, audit log export, on-premise engine deployment and SLAs.",
    email: "enterprise@xforcemsw.com",
  },
];

export default async function ContactPage() {
  const engine = await engineStatus();

  return (
    <MarketingShell>
      <PageHeader
        eyebrow="Contact"
        title="Tell us about your drawings"
        description="We are a small team and we read every message. Tell us the source format, the target you need and roughly how many drawings a month go through the process."
      />

      <section className="mx-auto grid max-w-6xl gap-8 px-5 py-16 lg:grid-cols-[1.5fr_1fr] lg:items-start">
        <Panel>
          <PanelTitle title="Send a message" description="We reply within one business day." />
          <div className="p-5 sm:p-6">
            <ContactForm />
          </div>
        </Panel>

        <div className="space-y-6">
          {ROUTES.map((route) => (
            <Panel key={route.title} className="p-5">
              <p className="text-sm font-semibold text-mist-100">{route.title}</p>
              <p className="mt-1.5 text-xs leading-relaxed text-mist-400">{route.body}</p>
              <p className="mt-3 font-mono text-xs text-signal-300">{route.email}</p>
            </Panel>
          ))}

          <Panel className="p-5">
            <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-mist-500">
              Platform status
            </p>
            <p className="mt-3 text-xs leading-relaxed text-mist-400">{engine.detail}</p>
          </Panel>
        </div>
      </section>
    </MarketingShell>
  );
}
