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
  priority: string;
  features: string[];
}

// The engine rejects anything above MSW_MAX_UPLOAD_BYTES (32 MB), so no plan may
// promise a larger file — the server would fail the conversion after accepting it.
const ENGINE_MAX_UPLOAD_BYTES = 32 * 1024 * 1024;

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
    priority: "Standard queue",
    features: [
      "5 conversions per month",
      "Single-file uploads up to 5 MB",
      "7-day result retention",
      "All six artifacts per job",
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
    priority: "Standard queue",
    features: [
      "150 conversions per month",
      "Single-file uploads up to 25 MB",
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
    maxUploadBytes: ENGINE_MAX_UPLOAD_BYTES,
    seats: 5,
    retentionDays: 180,
    licenseKeys: 5,
    priority: "Priority queue",
    features: [
      "1,000 conversions per month",
      "Single-file uploads up to 32 MB",
      "5 license keys, 5 seats",
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
    maxUploadBytes: ENGINE_MAX_UPLOAD_BYTES,
    seats: 25,
    retentionDays: 730,
    licenseKeys: 50,
    priority: "Dedicated capacity",
    features: [
      "Unlimited conversions",
      "25 seats and 50 license keys",
      "Single-file uploads up to 32 MB",
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
  /** null mints an unassigned key that belongs to nobody until a seat is claimed. */
  userId: string | null;
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

/**
 * Every license an account can use: the ones issued to it, plus the ones it
 * holds a seat on. The second half is what makes an unassigned admin-minted key
 * work — `licenses.user_id` stays null on those, so ownership alone is not
 * enough to find them.
 */
export function listLicenses(userId: string): License[] {
  return getDb()
    .prepare(
      `SELECT DISTINCT l.* FROM licenses l
       LEFT JOIN license_seats s ON s.license_id = l.id AND s.account_id = ?
       WHERE l.user_id = ? OR s.account_id = ?
       ORDER BY l.created_at DESC`,
    )
    .all(userId, userId, userId) as License[];
}

export function listAllLicenses(limit = 200): License[] {
  return getDb()
    .prepare("SELECT * FROM licenses ORDER BY created_at DESC LIMIT ?")
    .all(limit) as License[];
}

export interface LicenseWithOwner extends License {
  owner_email: string | null;
  owner_name: string | null;
  seats_used: number;
  expired: number;
}

/** Admin view: every license with its owner and live seat usage. */
export function listLicensesForAdmin(limit = 200): LicenseWithOwner[] {
  return getDb()
    .prepare(
      `SELECT l.*,
              u.email AS owner_email,
              u.name  AS owner_name,
              (SELECT COUNT(*) FROM license_seats s WHERE s.license_id = l.id) AS seats_used,
              CASE WHEN l.expires_at IS NOT NULL AND l.expires_at <= ? THEN 1 ELSE 0 END AS expired
       FROM licenses l
       LEFT JOIN users u ON u.id = l.user_id
       ORDER BY l.created_at DESC
       LIMIT ?`,
    )
    .all(nowIso(), limit) as LicenseWithOwner[];
}

export interface IssueForUserInput {
  /** Empty mints an unassigned key. A non-empty address must already have an account. */
  ownerEmail: string;
  plan: PlanId;
  seats?: number;
  expiresAt?: string | null;
  notes?: string | null;
}

export type IssueForUserResult =
  | { ok: true; license: License }
  | { ok: false; error: "user_not_found" | "invalid_plan" };

/**
 * Issue a license from the admin UI.
 *
 * With an email, the license belongs to that account straight away. Without one,
 * the key is minted unassigned: it works for whoever holds it and becomes tied
 * to an account the first time someone claims a seat with it.
 */
export function issueLicenseForUser(input: IssueForUserInput): IssueForUserResult {
  const email = input.ownerEmail.trim().toLowerCase();
  if (!(PLAN_ORDER as string[]).includes(input.plan)) return { ok: false, error: "invalid_plan" };

  let ownerId: string | null = null;
  if (email) {
    const owner = getDb().prepare("SELECT id FROM users WHERE email = ?").get(email) as { id: string } | undefined;
    if (!owner) return { ok: false, error: "user_not_found" };
    ownerId = owner.id;
  }

  const plan = PLANS[input.plan];
  const seats = input.seats && input.seats > 0 ? Math.min(input.seats, 500) : plan.seats;

  const license = createLicense({
    userId: ownerId,
    plan: input.plan,
    seats,
    activationLimit: seats,
    expiresAt: input.expiresAt ?? null,
    notes: input.notes ?? null,
  });

  return { ok: true, license };
}

export interface LicenseTerms {
  seats?: number;
  expiresAt?: string | null;
  notes?: string | null;
}

/** Change the seat allowance, expiry or notes on an existing license. */
export function updateLicenseTerms(id: string, terms: LicenseTerms): boolean {
  const db = getDb();
  const license = db.prepare("SELECT * FROM licenses WHERE id = ?").get(id) as License | undefined;
  if (!license) return false;

  const sets: string[] = [];
  const values: unknown[] = [];

  if (terms.seats !== undefined) {
    const seats = Math.max(1, Math.min(terms.seats, 500));
    sets.push("seats = ?", "activation_limit = ?");
    values.push(seats, seats);
  }
  if (terms.expiresAt !== undefined) {
    sets.push("expires_at = ?");
    values.push(terms.expiresAt);
  }
  if (terms.notes !== undefined) {
    sets.push("notes = ?");
    values.push(terms.notes);
  }

  if (sets.length > 0) {
    db.prepare(`UPDATE licenses SET ${sets.join(", ")} WHERE id = ?`).run(...values, id);
  }

  const parts: string[] = [];
  if (terms.seats !== undefined) parts.push(`seats=${terms.seats}`);
  if (terms.expiresAt !== undefined) parts.push(`expires=${terms.expiresAt ?? "never"}`);
  if (terms.notes !== undefined) parts.push("notes=updated");
  recordLicenseEvent(id, "terms_updated", parts.join(" ") || null);

  return true;
}

export function countActiveLicenses(userId: string): number {
  const row = getDb()
    .prepare(
      `SELECT COUNT(DISTINCT l.id) AS n FROM licenses l
       LEFT JOIN license_seats s ON s.license_id = l.id AND s.account_id = ?
       WHERE (l.user_id = ? OR s.account_id = ?)
         AND l.status = 'active'
         AND (l.expires_at IS NULL OR l.expires_at > ?)`,
    )
    .get(userId, userId, userId, nowIso()) as { n: number };
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
  seatsRemaining?: number;
}

export function checkLicenseKey(rawKey: string): LicenseCheck {
  const key = normalizeKey(rawKey);
  if (!key) return { valid: false, reason: "missing_key", license: null };

  const license = getDb().prepare("SELECT * FROM licenses WHERE key = ?").get(key) as License | undefined;
  if (!license) return { valid: false, reason: "unknown_key", license: null };
  if (license.status === "revoked") return { valid: false, reason: "revoked", license };
  if (license.status === "suspended") return { valid: false, reason: "suspended", license };
  if (isExpired(license.expires_at)) return { valid: false, reason: "expired", license };

  // A full license is still a valid key: seats are first-come first-served, so
  // whether a specific holder can take one is decided at claim time, not here.
  // The previous check rejected the key for everyone — including the owner.
  const seatsRemaining = Math.max(0, license.activation_limit - countSeats(license.id));
  return { valid: true, reason: null, license, seatsRemaining };
}

export interface LicenseSeat {
  id: string;
  license_id: string;
  holder: string;
  holder_kind: string;
  account_id: string | null;
  claimed_at: string;
  last_used_at: string | null;
}

export function countSeats(licenseId: string): number {
  const row = getDb()
    .prepare("SELECT COUNT(*) AS n FROM license_seats WHERE license_id = ?")
    .get(licenseId) as { n: number };
  return row.n;
}

export function listSeats(licenseId: string): LicenseSeat[] {
  return getDb()
    .prepare("SELECT * FROM license_seats WHERE license_id = ? ORDER BY claimed_at ASC")
    .all(licenseId) as LicenseSeat[];
}

/** Licenses this account holds a seat on, including ones issued to someone else. */
export function listSeatedLicenses(accountId: string): License[] {
  return getDb()
    .prepare(
      `SELECT l.* FROM licenses l
       JOIN license_seats s ON s.license_id = l.id
       WHERE s.account_id = ?
       ORDER BY l.created_at DESC`,
    )
    .all(accountId) as License[];
}

export function seatHolder(licenseId: string, holder: string): LicenseSeat | undefined {
  return getDb()
    .prepare("SELECT * FROM license_seats WHERE license_id = ? AND holder = ?")
    .get(licenseId, holder) as LicenseSeat | undefined;
}

export interface ClaimResult {
  ok: boolean;
  reason: "claimed" | "already_held" | "activation_limit" | "not_found" | null;
  seatsUsed: number;
  seatLimit: number;
}

function syncActivationCount(licenseId: string): void {
  getDb()
    .prepare("UPDATE licenses SET activations = (SELECT COUNT(*) FROM license_seats WHERE license_id = ?), last_used_at = ? WHERE id = ?")
    .run(licenseId, nowIso(), licenseId);
}

/**
 * Claim one seat for a holder. Idempotent: a holder that already has a seat just
 * refreshes last_used_at and does not consume another one, so repeated uploads
 * or client polling cannot burn through the seat allowance.
 */
export function claimSeat(
  licenseId: string,
  holder: string,
  holderKind: "account" | "device" = "account",
  accountId: string | null = null,
): ClaimResult {
  const db = getDb();
  const license = db.prepare("SELECT * FROM licenses WHERE id = ?").get(licenseId) as License | undefined;
  if (!license) return { ok: false, reason: "not_found", seatsUsed: 0, seatLimit: 0 };

  const normalized = holder.trim().toLowerCase();
  if (!normalized) return { ok: false, reason: "not_found", seatsUsed: 0, seatLimit: license.activation_limit };

  const existing = seatHolder(licenseId, normalized);
  const now = nowIso();

  if (existing) {
    db.prepare("UPDATE license_seats SET last_used_at = ? WHERE id = ?").run(now, existing.id);
    db.prepare("UPDATE licenses SET last_used_at = ? WHERE id = ?").run(now, licenseId);
    return {
      ok: true,
      reason: "already_held",
      seatsUsed: countSeats(licenseId),
      seatLimit: license.activation_limit,
    };
  }

  if (countSeats(licenseId) >= license.activation_limit) {
    return {
      ok: false,
      reason: "activation_limit",
      seatsUsed: countSeats(licenseId),
      seatLimit: license.activation_limit,
    };
  }

  db.prepare(
    "INSERT INTO license_seats (id, license_id, holder, holder_kind, account_id, claimed_at, last_used_at) VALUES (?, ?, ?, ?, ?, ?, ?)",
  ).run(newId("seat"), licenseId, normalized, holderKind, accountId, now, now);

  recordLicenseEvent(licenseId, "seat_claimed", `${normalized} (${holderKind})`);
  syncActivationCount(licenseId);

  return { ok: true, reason: "claimed", seatsUsed: countSeats(licenseId), seatLimit: license.activation_limit };
}

export function releaseSeat(licenseId: string, holder: string): boolean {
  const db = getDb();
  const normalized = holder.trim().toLowerCase();
  const info = db
    .prepare("DELETE FROM license_seats WHERE license_id = ? AND holder = ?")
    .run(licenseId, normalized);
  if (info.changes === 0) return false;
  recordLicenseEvent(licenseId, "seat_released", normalized);
  syncActivationCount(licenseId);
  return true;
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
