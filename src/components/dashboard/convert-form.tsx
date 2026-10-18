"use client";

import { useRouter } from "next/navigation";
import { useMemo, useRef, useState } from "react";
import { Button, Field, cx, inputClass } from "@/components/ui";
import { formatBytes } from "@/lib/util";

// Keep in step with ALLOWED_EXTENSIONS in src/app/api/jobs/route.ts — every source
// family the engine's PrepareSourceAsync can normalize to TriStation XML.
const ACCEPT = ".pdf,.txt,.lpt,.lst,.log,.csv,.xml,.zip,.pt2,.pt2x,.ap16,.ap17,.ap18,.ap19,.ap20";

type Target = "tia" | "pt2x" | "xml";

const TARGETS: Array<{ id: Target; label: string; detail: string }> = [
  {
    id: "tia",
    label: "TIA package",
    detail: "SimaticML zip + import script for TIA Portal (default)",
  },
  {
    id: "pt2x",
    label: "PT2X",
    detail: "Loadable TriStation container",
  },
  {
    id: "xml",
    label: "TriStation XML",
    detail: "PLCopen interchange XML",
  },
];

function sourceFamily(name: string): string {
  const ext = name.includes(".") ? name.split(".").pop()!.toLowerCase() : "";
  if (ext === "pdf") return "Ladder PDF";
  if (ext === "lpt" || ext === "txt" || ext === "lst" || ext === "log" || ext === "csv") return "TRISTATION listing";
  if (ext === "xml") return "XML export";
  if (ext === "zip" || ext.startsWith("ap")) return "TIA export";
  if (ext === "pt2" || ext === "pt2x") return "TriStation project";
  return "Source file";
}

export function ConvertForm({ maxUploadBytes }: { maxUploadBytes: number }) {
  const router = useRouter();
  const formRef = useRef<HTMLFormElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [target, setTarget] = useState<Target>("tia");
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const [dragActive, setDragActive] = useState(false);

  const tooLarge = file !== null && file.size > maxUploadBytes;
  const family = useMemo(() => (file ? sourceFamily(file.name) : null), [file]);

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    if (!file) {
      setError("Choose a source file first.");
      return;
    }
    if (tooLarge) {
      setError(`That file is larger than the ${formatBytes(maxUploadBytes)} limit on your plan.`);
      return;
    }

    const formData = new FormData(formRef.current ?? undefined);
    if (!formData.has("file")) formData.set("file", file);
    formData.set("target", target);

    setProgress(0);

    try {
      const response = await fetch("/api/jobs", { method: "POST", body: formData });
      const payload = (await response.json()) as { id?: string; error?: string };

      if (!response.ok || !payload.id) {
        setError(payload.error ?? "The conversion could not be started.");
        setProgress(null);
        return;
      }

      setProgress(100);
      router.push(`/dashboard/jobs/${payload.id}`);
      router.refresh();
    } catch {
      setError("Network error while uploading. Check your connection and try again.");
      setProgress(null);
    }
  }

  return (
    <form ref={formRef} onSubmit={handleSubmit} className="space-y-5" noValidate>
      {error ? (
        <p
          role="alert"
          className="rounded-lg border border-bad-500/40 bg-bad-500/10 px-3.5 py-2.5 text-xs text-bad-500"
        >
          {error}
        </p>
      ) : null}

      {/* ---- Dropzone ---- */}
      <Field
        label="Source file"
        htmlFor="file"
        hint={`Ladder PDF, TRISTATION listing, TriStation XML or project, or a Siemens TIA export. Max ${formatBytes(maxUploadBytes)}.`}
      >
        <div
          onDragOver={(event) => {
            event.preventDefault();
            setDragActive(true);
          }}
          onDragLeave={() => setDragActive(false)}
          onDrop={(event) => {
            event.preventDefault();
            setDragActive(false);
            const dropped = event.dataTransfer.files?.[0];
            if (dropped) setFile(dropped);
          }}
          onClick={() => fileInputRef.current?.click()}
          role="button"
          tabIndex={0}
          onKeyDown={(event) => {
            if (event.key === "Enter" || event.key === " ") fileInputRef.current?.click();
          }}
          className={cx(
            "cursor-pointer rounded-lg border-2 border-dashed px-5 py-8 text-center transition-colors",
            file
              ? "border-signal-500/60 bg-signal-500/5"
              : dragActive
                ? "border-signal-500 bg-signal-500/10"
                : "border-ink-600 bg-ink-900/60 hover:border-signal-500/60",
          )}
        >
          <input
            ref={fileInputRef}
            id="file"
            name="file"
            type="file"
            accept={ACCEPT}
            onChange={(event) => setFile(event.target.files?.[0] ?? null)}
            className="hidden"
          />
          {file ? (
            <div>
              <p className="font-mono text-xs text-mist-200">{file.name}</p>
              <p className="mt-1.5 text-[11px] text-mist-500">
                {family} · {formatBytes(file.size)}
                {tooLarge ? <span className="ml-2 text-bad-500">exceeds plan limit</span> : null}
              </p>
              <button
                type="button"
                onClick={(event) => {
                  event.stopPropagation();
                  setFile(null);
                }}
                className="mt-2 text-[11px] text-mist-400 underline hover:text-mist-200"
              >
                Choose a different file
              </button>
            </div>
          ) : (
            <div>
              <p className="text-sm font-medium text-mist-200">Drop your file here, or click to browse</p>
              <p className="mt-1.5 text-[11px] text-mist-500">
                PDF · LPT · TXT · XML · ZIP · PT2/PT2X · AP16–AP20
              </p>
            </div>
          )}
        </div>
      </Field>

      {/* ---- Output format ---- */}
      <fieldset>
        <legend className="text-[11px] font-medium uppercase tracking-[0.14em] text-mist-500">
          Output format
        </legend>
        <div className="mt-3 grid gap-2.5">
          {TARGETS.map((option) => {
            const selected = target === option.id;
            return (
              <label
                key={option.id}
                className={cx(
                  "flex cursor-pointer items-start gap-3 rounded-lg border px-4 py-3 transition-colors",
                  selected
                    ? "border-signal-500 bg-signal-500/10"
                    : "border-ink-700 bg-ink-900/40 hover:border-ink-600",
                )}
              >
                <input
                  type="radio"
                  name="target"
                  value={option.id}
                  checked={selected}
                  onChange={() => setTarget(option.id)}
                  className="mt-0.5 accent-signal-500"
                />
                <span>
                  <span className={cx("block text-xs font-semibold", selected ? "text-mist-100" : "text-mist-300")}>
                    {option.label}
                  </span>
                  <span className="mt-0.5 block text-[11px] text-mist-500">{option.detail}</span>
                </span>
              </label>
            );
          })}
        </div>
      </fieldset>

      <div className="grid gap-5 sm:grid-cols-2">
        <Field label="License key" htmlFor="licenseKey" hint="Optional. Defaults to your first active key.">
          <input
            id="licenseKey"
            name="licenseKey"
            className={inputClass}
            placeholder="XMSW-STA-XXXXX-XXXXX-XXXXX-XXXXX"
            autoComplete="off"
            spellCheck={false}
          />
        </Field>

        <Field label="Notes for the job" htmlFor="notes" hint="Stored with the job record.">
          <input id="notes" name="notes" className={inputClass} placeholder="Panel 3, line 2" />
        </Field>
      </div>

      {progress !== null ? (
        <div>
          <div className="h-1.5 overflow-hidden rounded-full bg-ink-800">
            <div
              className="h-full rounded-full bg-gradient-to-r from-signal-600 to-signal-400 transition-[width]"
              style={{ width: `${Math.max(5, progress)}%` }}
            />
          </div>
          <p className="mt-2 text-[11px] text-mist-500">
            {progress < 100 ? "Uploading and converting…" : "Conversion finished, opening job…"}
          </p>
        </div>
      ) : null}

      <Button type="submit" disabled={progress !== null || !file || tooLarge} className="w-full sm:w-auto">
        {progress !== null ? "Working…" : "Convert"}
      </Button>
    </form>
  );
}
