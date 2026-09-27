import fs from "node:fs";
import path from "node:path";
import { NextResponse } from "next/server";
import { getCurrentUser } from "@/lib/auth";
import { getJobForUser, outputsDir } from "@/lib/jobs";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET(
  _request: Request,
  { params }: RouteContext<"/api/jobs/[id]/output/[index]">,
) {
  const user = await getCurrentUser();
  if (!user) return NextResponse.json({ error: "Sign in required." }, { status: 401 });

  const { id, index } = await params;
  const job = getJobForUser(id, user.id);
  if (!job) return NextResponse.json({ error: "Job not found." }, { status: 404 });

  const position = Number(index);
  const output = job.outputs[position];
  if (!Number.isInteger(position) || !output) {
    return NextResponse.json({ error: "Output not found." }, { status: 404 });
  }

  const resolved = path.resolve(output.path);
  const allowedRoot = path.resolve(outputsDir(user.id, job.id));

  if (!resolved.startsWith(allowedRoot + path.sep)) {
    return NextResponse.json({ error: "Refusing to serve a path outside the job folder." }, { status: 403 });
  }

  if (!fs.existsSync(resolved)) {
    return NextResponse.json({ error: "The stored file is no longer available." }, { status: 410 });
  }

  const bytes = fs.readFileSync(resolved);

  return new NextResponse(new Uint8Array(bytes), {
    headers: {
      "Content-Type": output.mimeType || "application/octet-stream",
      "Content-Length": String(bytes.byteLength),
      "Content-Disposition": `attachment; filename="${output.name.replace(/"/g, "")}"`,
      "Cache-Control": "private, no-store",
    },
  });
}
