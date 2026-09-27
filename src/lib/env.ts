const isProd = process.env.NODE_ENV === "production";

let cachedSecret: string | null = null;

function read(name: string): string | undefined {
  const value = process.env[name];
  if (typeof value === "string" && value.trim().length > 0) return value.trim();
  return undefined;
}

function requireInProduction(name: string, devFallback: string): string {
  const value = read(name);
  if (value) return value;
  if (isProd) {
    throw new Error(
      `Missing required environment variable ${name}. Set it in your host's environment before starting the server.`,
    );
  }
  return devFallback;
}

export function sessionSecret(): string {
  if (cachedSecret) return cachedSecret;
  cachedSecret = requireInProduction("SESSION_SECRET", "dev-only-insecure-session-secret");
  return cachedSecret;
}

export function databasePath(): string {
  return requireInProduction("DATABASE_PATH", "./data/xforcemsw.db");
}

export function storageDir(): string {
  return requireInProduction("STORAGE_DIR", "./data/storage");
}

export function appUrl(): string {
  return read("APP_URL") ?? (isProd ? "" : "http://localhost:3000");
}

export function siteName(): string {
  return read("SITE_NAME") ?? "XforceMsw";
}

export function isProduction(): boolean {
  return isProd;
}

export function adminEmails(): string[] {
  const raw = read("ADMIN_EMAILS") ?? read("ADMIN_EMAIL") ?? "";
  return raw
    .split(",")
    .map((entry) => entry.trim().toLowerCase())
    .filter((entry) => entry.length > 0);
}

export function engineUrl(): string | null {
  return read("MSW_ENGINE_URL") ?? null;
}

export function engineToken(): string | null {
  return read("MSW_ENGINE_TOKEN") ?? null;
}
