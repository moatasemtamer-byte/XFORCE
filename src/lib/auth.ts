import { cookies } from "next/headers";
import { getDb } from "./db";
import { hashToken } from "./password";
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
    .run(newId("aud"), actorId, action, detail === undefined ? null : JSON.stringify(detail), nowIso());
}
