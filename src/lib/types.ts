export type UserRole = "user" | "admin";
export type PlanId = "free" | "starter" | "professional" | "enterprise";
export type JobStatus = "queued" | "processing" | "completed" | "failed";
export type LicenseStatus = "active" | "suspended" | "revoked" | "expired";

export interface User {
  id: string;
  email: string;
  name: string;
  company: string | null;
  password_hash: string;
  role: UserRole;
  plan: PlanId;
  created_at: string;
  updated_at: string;
}

export interface PublicUser {
  id: string;
  email: string;
  name: string;
  company: string | null;
  role: UserRole;
  plan: PlanId;
  created_at: string;
}

/**
 * Named capabilities a license can grant. Access is decided entirely by this
 * list — the plan is only a label and a set of numeric limits.
 */
export type LicenseFeature =
  | "convert.xml"
  | "convert.pt2x"
  | "convert.tia"
  | "viewer"
  | "source.pdf"
  | "source.listing"
  | "artifacts.netlist"
  | "artifacts.rungs_report"
  | "artifacts.bom"
  | "project.build";

export interface License {
  id: string;
  /** null until someone claims a seat: an unassigned key belongs to nobody yet. */
  user_id: string | null;
  key: string;
  plan: PlanId;
  status: LicenseStatus;
  seats: number;
  activations: number;
  activation_limit: number;
  /** JSON-encoded LicenseFeature[] granting what this key may do. */
  features: string;
  notes: string | null;
  created_at: string;
  expires_at: string | null;
  last_used_at: string | null;
}

export interface JobOutput {
  name: string;
  path: string;
  size: number;
  mimeType: string;
  kind: string;
}

export interface Job {
  id: string;
  user_id: string;
  license_id: string | null;
  status: JobStatus;
  engine: string;
  source_name: string;
  source_size: number;
  source_path: string | null;
  options: string;
  outputs: string;
  warnings: string;
  error: string | null;
  progress: number;
  created_at: string;
  started_at: string | null;
  finished_at: string | null;
}

export interface DecodedJob extends Omit<Job, "options" | "outputs" | "warnings"> {
  options: Record<string, unknown>;
  outputs: JobOutput[];
  warnings: string[];
}
