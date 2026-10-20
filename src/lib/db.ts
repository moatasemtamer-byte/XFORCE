import fs from "node:fs";
import path from "node:path";
import Database from "better-sqlite3";
import { databasePath } from "./env";

// Kept inline (rather than imported from ./licensing) to avoid a circular import
// between the DB bootstrap and the licensing module. Must stay in sync with
// ALL_FEATURES in licensing.ts.
const ALL_FEATURES_JSON = JSON.stringify([
  "convert.xml",
  "convert.pt2x",
  "convert.tia",
  "project.build",
  "viewer",
  "source.pdf",
  "source.listing",
  "artifacts.netlist",
  "artifacts.rungs_report",
  "artifacts.bom",
]);

export type Sqlite = Database.Database;

let instance: Sqlite | null = null;

function loadSchema(): string {
  const file = path.join(process.cwd(), "src", "lib", "schema.sql");
  if (!fs.existsSync(file)) {
    throw new Error(
      `Database schema not found at ${file}. The application must run from the project root so src/lib/schema.sql is readable.`,
    );
  }
  return fs.readFileSync(file, "utf8");
}

/**
 * Repairs databases created before `licenses.user_id` became nullable.
 *
 * SQLite cannot drop a NOT NULL constraint in place, so the table is rebuilt.
 * Runs on every cold start but is a no-op once the column is already nullable,
 * which `PRAGMA table_info` tells us without touching any rows.
 */
function applyMigrations(db: Sqlite): void {
  const columns = db.prepare("PRAGMA table_info(licenses)").all() as { name: string; notnull: number }[];
  const userId = columns.find((c) => c.name === "user_id");
  if (userId && userId.notnull === 1) {
    // The pragma is a no-op inside a transaction, so it has to be turned off
    // first. Child rows in license_events/license_seats are deliberately left
    // alone: they keep pointing at `licenses`, which the rename below recreates.
    db.pragma("foreign_keys = OFF");
    try {
      db.transaction(() => {
        db.exec(`
          CREATE TABLE licenses_migrated (
            id TEXT PRIMARY KEY,
            user_id TEXT REFERENCES users(id) ON DELETE CASCADE,
            key TEXT NOT NULL UNIQUE,
            plan TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'active',
            seats INTEGER NOT NULL DEFAULT 1,
            activations INTEGER NOT NULL DEFAULT 0,
            activation_limit INTEGER NOT NULL DEFAULT 1,
            features TEXT NOT NULL DEFAULT '["viewer"]',
            notes TEXT,
            created_at TEXT NOT NULL,
            expires_at TEXT,
            last_used_at TEXT
          );
          INSERT INTO licenses_migrated
            SELECT id, user_id, key, plan, status, seats, activations,
                   activation_limit, notes, created_at, expires_at, last_used_at
            FROM licenses;
          DROP TABLE licenses;
          ALTER TABLE licenses_migrated RENAME TO licenses;
        `);
      })();

      // Indexes went with the old table.
      db.exec(`
        CREATE INDEX IF NOT EXISTS idx_licenses_user ON licenses(user_id);
        CREATE INDEX IF NOT EXISTS idx_licenses_key ON licenses(key);
      `);

      const broken = db.pragma("foreign_key_check") as { table: string }[];
      if (broken.length > 0) {
        throw new Error(`licenses migration left broken references: ${broken.map((b) => b.table).join(", ")}`);
      }
    } finally {
      db.pragma("foreign_keys = ON");
    }
  }

  // Databases created before per-license features: add the column, then grant
  // every feature to existing rows so a deploy never silently locks anyone out.
  const refreshed = db.prepare("PRAGMA table_info(licenses)").all() as { name: string }[];
  if (!refreshed.some((c) => c.name === "features")) {
    db.exec(`ALTER TABLE licenses ADD COLUMN features TEXT NOT NULL DEFAULT '["viewer"]'`);
    db.prepare("UPDATE licenses SET features = ?").run(ALL_FEATURES_JSON);
  }
}

export function getDb(): Sqlite {
  if (instance) return instance;

  const file = databasePath();
  if (file !== ":memory:") {
    fs.mkdirSync(path.dirname(path.resolve(file)), { recursive: true });
  }

  const db = new Database(file);
  db.pragma("journal_mode = WAL");
  db.pragma("foreign_keys = ON");
  db.pragma("busy_timeout = 5000");
  db.exec(loadSchema());
  applyMigrations(db);
  instance = db;
  return db;
}
