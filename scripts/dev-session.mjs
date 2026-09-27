/**
 * Dev-only helper: create (or promote) a local user and mint a session cookie
 * so you can test authenticated pages without going through the login form.
 *
 *   node scripts/dev-session.mjs you@example.com Secret1234 "Your Name" admin
 *
 * Refuses to run when NODE_ENV=production.
 */
import { createHash, randomBytes, randomUUID, scryptSync } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import Database from "better-sqlite3";

if (process.env.NODE_ENV === "production") {
  console.error("Refusing to run in production.");
  process.exit(1);
}

const [emailArg, passwordArg, nameArg, roleArg] = process.argv.slice(2);

if (!emailArg || !passwordArg) {
  console.error('Usage: node scripts/dev-session.mjs <email> <password> [name] [role]');
  process.exit(1);
}

const email = emailArg.trim().toLowerCase();
const name = nameArg?.trim() || email.split("@")[0];
const role = roleArg === "admin" ? "admin" : "user";
const dbPath = process.env.DATABASE_PATH || "./data/xforcemsw.db";

const salt = randomBytes(16);
const derived = scryptSync(passwordArg, salt, 64, { N: 16384, r: 8, p: 1 });
const passwordHash = `scrypt$16384$8$1$${salt.toString("base64")}$${derived.toString("base64")}`;

const resolvedDb = path.resolve(dbPath);
fs.mkdirSync(path.dirname(resolvedDb), { recursive: true });
const db = new Database(resolvedDb);
db.pragma("foreign_keys = ON");
db.exec(fs.readFileSync(path.resolve("src/lib/schema.sql"), "utf8"));

const now = new Date().toISOString();
const userId = `usr_${randomUUID().replace(/-/g, "")}`;

const existing = db.prepare("SELECT id FROM users WHERE email = ?").get(email);

const user = existing
  ? (db
      .prepare("UPDATE users SET password_hash = ?, role = ?, updated_at = ? WHERE id = ? RETURNING id")
      .get(passwordHash, role, now, existing.id))
  : (db
      .prepare(
        "INSERT INTO users (id, email, name, password_hash, role, plan, created_at, updated_at) VALUES (?, ?, ?, ?, ?, 'professional', ?, ?) RETURNING id",
      )
      .get(userId, email, name, passwordHash, role, now, now));

const token = randomBytes(32).toString("base64url");
const expiresAt = new Date(Date.now() + 7 * 86_400_000).toISOString();

db.prepare(
  "INSERT INTO sessions (id, user_id, created_at, expires_at, user_agent, ip) VALUES (?, ?, ?, ?, 'dev-session script', '127.0.0.1')",
).run(createHash("sha256").update(token).digest("hex"), user.id, now, expiresAt);

console.log(`user   ${email} (${role}) id=${user.id}`);
console.log(`cookie xmsw_session=${token}`);
console.log(`db     ${dbPath}`);
