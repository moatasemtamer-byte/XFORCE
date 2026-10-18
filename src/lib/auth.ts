import { cookies } from "next/headers";
import { getDb } from "./db";
import { hashToken, isPasswordlessAccount, passwordlessHash } from "./password";
import { addDays, newId, nowIso, randomToken } from "./util";
import { isProduction, adminEmails } from "./env";
import type { PublicUser, User, UserRole } from "./types";

export const SESSION_COOKIE = "xmsw_session";
const SHORT_TTL_DAYS = 1;
const LONG_TTL_DAYS = 30;

export function toPublicUser(user: User): PublicUser {
  return {
    id: user.id,
    email: user.email,
    name: user.name,
    company: user.company,
    role: user.role,
    plan: user.plan,
    created_at: user.created_at,
  };
}

export function roleForEmail(email: string): UserRole {
  return adminEmails().includes(email.trim().toLowerCase()) ? "admin" : "user";
}

export async function createSession(
  userId: string,
  options: { remember?: boolean; userAgent?: string | null; ip?: string | null } = {},
): Promise<void> {
  const db = getDb();
  const token = randomToken();
  const ttl = options.remember ? LONG_TTL_DAYS : SHORT_TTL_DAYS;
  const expiresAt = addDays(ttl);

  db.prepare(
    "INSERT INTO sessions (id, user_id, created_at, expires_at, user_agent, ip) VALUES (?, ?, ?, ?, ?, ?)",
  ).run(hashToken(token), userId, nowIso(), expiresAt, options.userAgent ?? null, options.ip ?? null);

  db.prepare("DELETE FROM sessions WHERE expires_at < ?").run(nowIso());

  const store = await cookies();
  store.set(SESSION_COOKIE, token, {
    httpOnly: true,
    sameSite: "lax",
    secure: isProduction(),
    path: "/",
    maxAge: ttl * 24 * 60 * 60,
  });
}

export async function destroySession(): Promise<void> {
  const store = await cookies();
  const token = store.get(SESSION_COOKIE)?.value;
  if (token) {
    getDb().prepare("DELETE FROM sessions WHERE id = ?").run(hashToken(token));
  }
  store.delete(SESSION_COOKIE);
}

export async function getCurrentUser(): Promise<User | null> {
  const store = await cookies();
  const token = store.get(SESSION_COOKIE)?.value;
  if (!token) return null;

  const db = getDb();
  const row = db
    .prepare(
      `SELECT users.* FROM sessions
       JOIN users ON users.id = sessions.user_id
       WHERE sessions.id = ? AND sessions.expires_at > ?`,
    )
    .get(hashToken(token), nowIso()) as User | undefined;

  return row ?? null;
}

export async function getCurrentPublicUser(): Promise<PublicUser | null> {
  const user = await getCurrentUser();
  return user ? toPublicUser(user) : null;
}

export function audit(actorId: string | null, action: string, detail?: unknown): void {
  getDb()
    .prepare("INSERT INTO audit_log (id, actor_id, action, detail, created_at) VALUES (?, ?, ?, ?, ?)")
    .run(newId("aud"), actorId, detail === undefined ? null : JSON.stringify(detail), nowIso());
}

/**
 * Find or create the account a license key belongs to.
 *
 * License-first access means a visitor never fills in a registration form: the
 * key plus an email address is the whole credential. These accounts are stored
 * with a non-parsable passwordless marker, so they can never be signed into
 * through the password form — the key is the only way in. A returning visitor
 * who already has a password keeps it untouched, and callers must not open a
 * session for them without that password.
 */
export function provisionLicenseAccount(email: string, name?: string | null): User {
  const db = getDb();
  const normalized = email.trim().toLowerCase();

  const existing = db.prepare("SELECT * FROM users WHERE email = ?").get(normalized) as User | undefined;
  if (existing) return existing;

  const id = newId("usr");
  const label = name?.trim() || normalized.split("@")[0];

  db.prepare(
    `INSERT INTO users (id, email, name, company, password_hash, role, plan, created_at, updated_at)
     VALUES (?, ?, ?, NULL, ?, ?, 'free', ?, ?)`,
  ).run(
    id,
    normalized,
    label,
    passwordlessHash(),
    roleForEmail(normalized),
    nowIso(),
    nowIso(),
  );

  return db.prepare("SELECT * FROM users WHERE id = ?").get(id) as User;
}

/**
 * True when the account was created by the license-first flow and therefore has
 * no password. Callers must never open a session for an account that *does*
 * have a password on the strength of a license key alone.
 */
export function isPasswordless(user: Pick<User, "password_hash">): boolean {
  return isPasswordlessAccount(user.password_hash);
}
