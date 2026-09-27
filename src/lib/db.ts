import fs from "node:fs";
import path from "node:path";
import Database from "better-sqlite3";
import { databasePath } from "./env";

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
  instance = db;
  return db;
}
