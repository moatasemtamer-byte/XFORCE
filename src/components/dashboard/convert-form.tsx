"use client";

import { useRouter } from "next/navigation";
import { useRef, useState } from "react";
import { Button, Field, inputClass } from "@/components/ui";
import { formatBytes } from "@/lib/util";

const ACCEPT = ".sch,.lad,.xml,.edb,.txt,.csv,.pdf,.png,.jpg,.jpeg,.dwg,.dxf";

export function ConvertForm({ maxUploadBytes }: { maxUploadBytes: number }) {
  const router = useRouter();
  const formRef = useRef<HTMLFormElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState<number | null>(null);

  const tooLarge = file !== null && file.size > maxUploadBytes;

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    if (!file) {
      setError("Choose a ladder diagram file first.");
      return;
    }
    if (tooLarge) {
      setError(`That file is larger than the ${formatBytes(maxUploadBytes)} limit on your plan.`);
      return;
    }

    const formData = new FormData(formRef.current ?? undefined);
    if (!formData.has("file")) formData.set("file", file);

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

      <Field
        label="Ladder diagram"
        htmlFor="file"
        hint={`Accepted: SCH, LAD, XML, EDB, TXT, CSV, PDF, images, DWG, DXF. Max ${formatBytes(maxUploadBytes)}.`}
      >
        <div className="rounded-lg border border-dashed border-ink-600 bg-ink-900/60 p-5 text-center transition-colors focus-within:border-signal-500">
          <input
            id="file"
            name="file"
            type="file"
            accept={ACCEPT}
            onChange={(event) => setFile(event.target.files?.[0] ?? null)}
            className="block w-full cursor-pointer text-xs text-mist-400 file:mr-3 file:rounded-md file:border-0 file:bg-ink-800 file:px-3 file:py-1.5 file:text-xs file:font-medium file:text-mist-200 hover:file:bg-ink-700"
          />
          {file ? (
            <p className="mt-3 font-mono text-[11px] text-mist-500">
              {file.name} · {formatBytes(file.size)}
              {tooLarge ? <span className="ml-2 text-bad-500">exceeds plan limit</span> : null}
            </p>
          ) : (
            <p className="mt-3 text-[11px] text-mist-500">No file selected</p>
          )}
        </div>
      </Field>

      <div className="grid gap-5 sm:grid-cols-2">
        <Field label="Target format" htmlFor="targetFormat">
          <select id="targetFormat" name="targetFormat" defaultValue="msw" className={inputClass}>
            <option value="msw">MSW (default)</option>
            <option value="msw-v2">MSW v2</option>
          </select>
        </Field>

        <Field label="Sheet name" htmlFor="sheetName" hint="Optional, passed to the engine.">
          <input id="sheetName" name="sheetName" className={inputClass} placeholder="MOTOR_STARTERS" />
        </Field>
      </div>

      <fieldset className="rounded-lg border border-ink-700 bg-ink-900/40 p-4">
        <legend className="px-1.5 text-[11px] font-medium uppercase tracking-[0.14em] text-mist-500">
          Additional outputs
        </legend>
        <div className="grid gap-3 sm:grid-cols-3">
          {[
            ["includeRungsReport", "Rung report"],
            ["includeNetlist", "Netlist"],
            ["includeBom", "Bill of materials"],
          ].map(([name, label]) => (
            <label key={name} className="flex items-center gap-2.5 text-xs text-mist-300">
              <input
                type="checkbox"
                name={name}
                defaultChecked={name === "includeRungsReport" || name === "includeNetlist"}
                className="h-4 w-4 rounded border-ink-500 bg-ink-800 accent-signal-500"
              />
              {label}
            </label>
          ))}
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
        {progress !== null ? "Working…" : "Convert to MSW"}
      </Button>
    </form>
  );
}
