import { NextResponse } from "next/server";
import { getCurrentUser } from "@/lib/auth";
import { getDashboardContext } from "@/lib/dashboard";
import { createAndRunJob, QuotaError } from "@/lib/jobs";
import { checkLicenseKey, claimSeat } from "@/lib/licensing";
import { clientKey, rateLimit } from "@/lib/rate-limit";
import { DEFAULT_OPTIONS, type ConversionOptions } from "@/lib/converter";
import { clean } from "@/lib/validate";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

// The conversion engine reads two kinds of source: a ladder-logic PDF, or a raw
// monospace program listing (e.g. a Triconex TRISTATION .LPT printout). Accepting
// anything else would only produce an empty conversion, so the list is kept
// deliberately narrow and matches what the engine can actually read.
const ALLOWED_EXTENSIONS = [".pdf", ".txt", ".lpt", ".lst", ".log", ".csv", ".xml"];

function extensionOf(name: string): string {
  const index = name.lastIndexOf(".");
  return index === -1 ? "" : name.slice(index).toLowerCase();
}

export async function POST(request: Request) {
  const user = await getCurrentUser();
  if (!user) return NextResponse.json({ error: "Sign in to run conversions." }, { status: 401 });

  const limit = rateLimit(clientKey(["job", user.id]), 60, 60 * 60_000);
  if (!limit.ok) {
    return NextResponse.json(
      { error: "Too many conversion requests. Please slow down." },
      { status: 429 },
    );
  }

  let form: FormData;
  try {
    form = await request.formData();
  } catch {
    return NextResponse.json({ error: "Malformed upload." }, { status: 400 });
  }

  const file = form.get("file");
  if (!(file instanceof File)) {
    return NextResponse.json({ error: "Choose a source file to upload." }, { status: 400 });
  }

  if (!ALLOWED_EXTENSIONS.includes(extensionOf(file.name))) {
    return NextResponse.json(
      { error: `Unsupported file type. Accepted: ${ALLOWED_EXTENSIONS.join(", ")}.` },
      { status: 400 },
    );
  }

  const context = await getDashboardContext(user);

  const rawKey = clean(form.get("licenseKey"), 80);
  let licenseId: string | null = null;
  let licenseKey: string | null = null;

  if (rawKey) {
    const check = checkLicenseKey(rawKey);
    if (!check.valid || !check.license) {
      return NextResponse.json(
        { error: `License key rejected: ${check.reason ?? "invalid"}.` },
        { status: 400 },
      );
    }
    // A license is shareable up to its seat allowance: the first N distinct
    // accounts to use the key each hold a seat, and reuse does not consume more.
    const seat = claimSeat(check.license.id, user.email, "account", user.id);
    if (!seat.ok) {
      return NextResponse.json(
        {
          error: `This license has no free seats left (${seat.seatsUsed}/${seat.seatLimit} in use). Ask the license owner or an administrator to raise the seat count.`,
        },
        { status: 403 },
      );
    }
    licenseId = check.license.id;
    licenseKey = check.license.key;
  } else {
    const firstActive = context.licenses.find(
      (license) => license.status === "active" && license.activations < license.activation_limit,
    );
    if (firstActive) {
      const seat = claimSeat(firstActive.id, user.email, "account", user.id);
      if (seat.ok) {
        licenseId = firstActive.id;
        licenseKey = firstActive.key;
      }
    }
  }

  const options: ConversionOptions = {
    ...DEFAULT_OPTIONS,
    targetFormat: clean(form.get("targetFormat"), 20) || DEFAULT_OPTIONS.targetFormat,
    includeNetlist: form.get("includeNetlist") === "on",
    includeBom: form.get("includeBom") === "on",
    includeRungsReport: form.get("includeRungsReport") === "on",
    sheetName: clean(form.get("sheetName"), 60) || null,
    notes: clean(form.get("notes"), 500) || null,
  };

  try {
    const job = await createAndRunJob({
      user,
      licenseId,
      licenseKey,
      upload: {
        filename: file.name,
        mimeType: file.type,
        bytes: Buffer.from(await file.arrayBuffer()),
      },
      options,
      maxUploadBytes: context.plan.maxUploadBytes,
      monthlyConversions: context.plan.monthlyConversions,
      usedThisMonth: context.usedThisMonth,
      retentionDays: context.plan.retentionDays,
    });

    return NextResponse.json({ id: job.id, status: job.status, error: job.error }, { status: 201 });
  } catch (error) {
    if (error instanceof QuotaError) {
      return NextResponse.json({ error: error.message }, { status: 402 });
    }
    return NextResponse.json(
      { error: error instanceof Error ? error.message : "Conversion failed." },
      { status: 500 },
    );
  }
}
