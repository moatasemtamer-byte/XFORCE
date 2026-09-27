"use server";

import { headers } from "next/headers";
import { getDb } from "@/lib/db";
import { clientKey, rateLimit } from "@/lib/rate-limit";
import { clean, isEmail } from "@/lib/validate";
import { newId, nowIso } from "@/lib/util";

export interface ContactFormState {
  error?: string;
  success?: boolean;
}

export async function submitContactAction(
  _prev: ContactFormState,
  formData: FormData,
): Promise<ContactFormState> {
  const ip = (await headers()).get("x-forwarded-for")?.split(",")[0]?.trim() ?? null;
  const limit = rateLimit(clientKey(["contact", ip]), 4, 30 * 60_000);
  if (!limit.ok) return { error: "Too many messages from this address. Try again later." };

  const name = clean(formData.get("name"), 120);
  const email = clean(formData.get("email"), 254).toLowerCase();
  const subject = clean(formData.get("subject"), 160);
  const body = clean(formData.get("body"), 4000);

  if (name.length < 2) return { error: "Please tell us your name." };
  if (!isEmail(email)) return { error: "Please enter a valid email address." };
  if (body.length < 20) return { error: "Please add a little more detail (at least 20 characters)." };

  getDb()
    .prepare("INSERT INTO messages (id, name, email, subject, body, created_at) VALUES (?, ?, ?, ?, ?, ?)")
    .run(newId("msg"), name, email, subject || "General enquiry", body, nowIso());

  return { success: true };
}
