import { getDb } from "./db";
import { isExpired, newId, nowIso } from "./util";
import type { License, PlanId, PublicUser } from "./types";

export interface PlanDefinition {
  id: PlanId;
  name: string;
  tagline: string;
  priceMonthly: number | null;
  priceYearly: number | null;
  monthlyConversions: number;
  maxUploadBytes: number;
  seats: number;
  retentionDays: number;
  licenseKeys: number;
  batchUploads: boolean;
  apiAccess: boolean;
  priority: string;
  features: string[];
}

export const PLANS: Record<PlanId, PlanDefinition> = {
  free: {
    id: "free",
    name: "Free",
    tagline: "Try the pipeline on real drawings.",
    priceMonthly: 0,
    priceYearly: 0,
    monthlyConversions: 5,
    maxUploadBytes: 5 * 1024 * 1024,
    seats: 1,
    retentionDays: 7,
    licenseKeys: 0,
    batchUploads: false,
    apiAccess: false,
    priority: "Standard queue",
    features: [
      "5 conversions per month",
      "Single-file uploads up to 5 MB",
      "7-day result retention",
      "Community support",
    ],
  },
  starter: {
    id: "starter",
    name: "Starter",
    tagline: "For a single engineer doing the work.",
    priceMonthly: 39,
    priceYearly: 390,
    monthlyConversions: 150,
    maxUploadBytes: 25 * 1024 * 1024,
    seats: 1,
    retentionDays: 30,
    licenseKeys: 1,
    batchUploads: true,
    apiAccess: false,
    priority: "Standard queue",
    features: [
      "150 conversions per month",
      "Batch uploads up to 25 MB",
      "1 license key, 30-day retention",
      "Email support within 2 business days",
    ],
  },
  professional: {
    id: "professional",
    name: "Professional",
    tagline: "For controls teams shipping machines.",
    priceMonthly: 129,
    priceYearly: 1290,
    monthlyConversions: 1000,
    maxUploadBytes: 100 * 1024 * 1024,
    seats: 5,
    retentionDays: 180,
    licenseKeys: 5,
    batchUploads: true,
    apiAccess: true,
    priority: "Priority queue",
    features: [
      "1,000 conversions per month",
      "Batch uploads up to 100 MB",
      "5 license keys, 5 seats",
      "REST API access",
      "Priority queue, 180-day retention",
    ],
  },
  enterprise: {
    id: "enterprise",
    name: "Enterprise",
    tagline: "Site-wide licensing and onboarding.",
    priceMonthly: null,
    priceYearly: null,
    monthlyConversions: Number.POSITIVE_INFINITY,
    maxUploadBytes: 512 * 1024 * 1024,
    seats: 25,
    retentionDays: 730,
    licenseKeys: 50,
    batchUploads: true,
    apiAccess: true,
    priority: "Dedicated capacity",
    features: [
      "Unlimited conversions",
      "Unlimited seats and 50 license keys",
      "Up to 512 MB per file",
      "SSO, audit log export, SLA",
      "Named engineer for onboarding",
    ],
  },
};

export const PLAN_ORDER: PlanId[] = ["free", "starter", "professional", "enterprise"];

const CROCKFORD = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

export function generateLicenseKey(plan: PlanId): string {
  const groups: string[] = [];
  for (let group = 0; group < 4; group += 1) {
    let chunk = "";
    for (let index = 0; index < 5; index += 1) {
      chunk += CROCKFORD[Math.floor(Math.random() * CROCKFORD.length)];
    }
    groups.push(chunk);
  }
  return `XMSW-${plan.slice(0, 3).toUpperCase()}-${groups.join("-")}`;
}

export function normalizeKey(raw: string): string {
  return raw.trim().toUpperCase().replace(/\s+/g, "");
}

export interface CreateLicenseInput {
  userId: string;
  plan: PlanId;
  seats?: number;
  activationLimit?: number;
  expiresAt?: string | null;
  notes?: string | null;
}

export function createLicense(input: CreateLicenseInput): License {
  const db = getDb();
  const plan = PLANS[input.plan];
  const id = newId("lic");
  const createdAt = nowIso();

  const insert = db.prepare(
    `INSERT INTO licenses (id, user_id, key, plan, status, seats, activations, activation_limit, notes, created_at, expires_at, last_used_at)
     VALUES (?, ?, ?, ?, 'active', ?, 0, ?, ?, ?, ?, NULL)`,
  );

  let key = generateLicenseKey(input.plan);
  while (db.prepare("SELECT 1 FROM licenses WHERE key = ?").get(key)) {
    key = generateLicenseKey(input.plan);
  }

  insert.run(
    id,
    input.userId,
    key,
    input.plan,
    input.seats ?? plan.seats,
    input.activationLimit ?? plan.seats,
    input.notes ?? null,
    createdAt,
    input.expiresAt ?? null,
  );

  db.prepare("INSERT INTO license_events (id, license_id, event, detail, created_at) VALUES (?, ?, ?, ?, ?)").run(
    newId("evt"),
    id,
    "issued",
    `plan=${input.plan}`,
    createdAt,
  );

  return db.prepare("SELECT * FROM licenses WHERE id = ?").get(id) as License;
}

export function listLicenses(userId: string): License[] {
  return getDb()
    .prepare("SELECT * FROM licenses WHERE user_id = ? ORDER BY created_at DESC")
    .all(userId) as License[];
}

export function listAllLicenses(limit = 200): License[] {
  return getDb()
    .prepare("SELECT * FROM licenses ORDER BY created_at DESC LIMIT ?")
    .all(limit) as License[];
}

export function countActiveLicenses(userId: string): number {
  const row = getDb()
    .prepare(
      "SELECT COUNT(*) AS n FROM licenses WHERE user_id = ? AND status = 'active' AND (expires_at IS NULL OR expires_at > ?)",
    )
    .get(userId, nowIso()) as { n: number };
  return row.n;
}

export function setLicenseStatus(id: string, status: License["status"]): void {
  const db = getDb();
  db.prepare("UPDATE licenses SET status = ? WHERE id = ?").run(status, id);
  db.prepare("INSERT INTO license_events (id, license_id, event, detail, created_at) VALUES (?, ?, ?, ?, ?)").run(
    newId("evt"),
    id,
    status,
    null,
    nowIso(),
  );
}

export function recordLicenseEvent(licenseId: string, event: string, detail?: string | null): void {
  getDb()
    .prepare("INSERT INTO license_events (id, license_id, event, detail, created_at) VALUES (?, ?, ?, ?, ?)")
    .run(newId("evt"), licenseId, event, detail ?? null, nowIso());
}

export interface LicenseCheck {
  valid: boolean;
  reason: string | null;
  license: License | null;
}

export function checkLicenseKey(rawKey: string): LicenseCheck {
  const key = normalizeKey(rawKey);
  if (!key) return { valid: false, reason: "missing_key", license: null };

  const license = getDb().prepare("SELECT * FROM licenses WHERE key = ?").get(key) as License | undefined;
  if (!license) return { valid: false, reason: "unknown_key", license: null };
  if (license.status === "revoked") return { valid: false, reason: "revoked", license };
  if (license.status === "suspended") return { valid: false, reason: "suspended", license };
  if (isExpired(license.expires_at)) return { valid: false, reason: "expired", license };
  if (license.activations >= license.activation_limit) return { valid: false, reason: "activation_limit", license };

  return { valid: true, reason: null, license };
}

export function activateLicense(id: string, deviceLabel: string | null): void {
  const db = getDb();
  db.prepare("UPDATE licenses SET activations = activations + 1, last_used_at = ? WHERE id = ?").run(nowIso(), id);
  recordLicenseEvent(id, "activated", deviceLabel);
}

export function effectivePlan(user: Pick<PublicUser, "plan">, licenses: License[]): PlanDefinition {
  let best: PlanId = user.plan;
  for (const license of licenses) {
    if (license.status !== "active") continue;
    if (isExpired(license.expires_at)) continue;
    if (PLAN_ORDER.indexOf(license.plan) > PLAN_ORDER.indexOf(best)) best = license.plan;
  }
  return PLANS[best];
}

export function monthlyUsage(userId: string): number {
  const since = new Date();
  since.setUTCDate(1);
  since.setUTCHours(0, 0, 0, 0);
  const row = getDb()
    .prepare("SELECT COUNT(*) AS n FROM jobs WHERE user_id = ? AND created_at >= ?")
    .get(userId, since.toISOString()) as { n: number };
  return row.n;
}
