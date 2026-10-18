import { engineOutputDir, engineStatusDir, engineToken, engineUrl } from "./env";

// Evidence model shared with the engine's TriconexToTia.Status library and the
// WPF shell. Every value is derived from files on disk by the engine; this
// module never invents a status.

export type EvidenceStatus =
  | "verifiedInTia"
  | "unitTestOnly"
  | "unverified"
  | "notStarted"
  | "failed";

export interface StatusItem {
  kind: string;
  name: string;
  status: EvidenceStatus;
  evidencePath?: string | null;
  detail?: string | null;
}

export interface PhaseStatus {
  phase: number;
  name: string;
  status: EvidenceStatus;
  verifiedCount: number;
  totalCount: number;
}

export interface ReportItem {
  kind?: string;
  name?: string;
  block?: string;
  reason?: string;
  status?: string;
  sites?: number;
}

export interface ReportCounts {
  pous_total?: number;
  pous_converted?: number;
  globals_total?: number;
  globals_converted?: number;
  udts_total?: number;
  library_calls_total?: number;
  library_calls_auto?: number;
  library_calls_approximate?: number;
  library_calls_manual?: number;
  fatals?: number;
  warnings?: number;
  manual_actions?: number;
}

export interface KnownIssue {
  id?: string;
  description?: string;
  affected_blocks?: string[];
  count?: number | null;
  shared_address_count?: number | null;
  tags_affected?: number | null;
  status?: string;
}

export interface ReportJson {
  timestamp?: string;
  source_file?: string;
  source_version?: string;
  tia_target?: string;
  cpu_order_number?: string;
  counts?: ReportCounts;
  converted?: ReportItem[];
  approximated?: ReportItem[];
  manual?: ReportItem[];
  never_converted?: ReportItem[];
  known_issues?: KnownIssue[];
}

export interface RoundTripMessage {
  state?: string;
  description?: string;
  path?: string;
  text?: string;
}

export interface RoundTripObject {
  kind?: string;
  name?: string;
  file?: string;
  imported?: boolean;
  compiled?: boolean;
  phase?: number | null;
  messages?: RoundTripMessage[];
}

export interface RoundTripJson {
  timestamp?: string;
  tia_version?: string;
  cpu_order_number?: string;
  project_path?: string;
  compile_warnings?: number | null;
  objects?: RoundTripObject[];
}

export interface TestRunSummary {
  total: number;
  executed: number;
  passed: number;
  failed: number;
  skipped: number;
  outcome?: string | null;
}

export interface ProjectStatus {
  buildStatus: EvidenceStatus;
  conversionStatus: EvidenceStatus;
  phaseStatuses: EvidenceStatus[];
  phases: PhaseStatus[];
  unverifiedCount: number;
  unitTestOnlyCount: number;
  compileWarnings: number;
  openKnownIssues: number;
  evidenceTimestamp?: string | null;
  evidencePath?: string | null;
  items: StatusItem[];
  report?: ReportJson | null;
  roundTrip?: RoundTripJson | null;
  tests?: TestRunSummary | null;
  evidenceFiles: Record<string, string>;
}

export interface EngineStatusView {
  status: ProjectStatus;
  error: string | null;
}

export const EMPTY_STATUS: ProjectStatus = {
  buildStatus: "notStarted",
  conversionStatus: "notStarted",
  phaseStatuses: [],
  phases: [],
  unverifiedCount: 0,
  unitTestOnlyCount: 0,
  compileWarnings: 0,
  openKnownIssues: 0,
  evidenceTimestamp: null,
  evidencePath: null,
  items: [],
  report: null,
  roundTrip: null,
  tests: null,
  evidenceFiles: {},
};

function apiUrl(path: string): URL | null {
  const base = engineUrl();
  if (!base) return null;
  const url = new URL(base.replace(/\/+$/, "") + path);
  const output = engineOutputDir();
  const status = engineStatusDir();
  if (output) url.searchParams.set("output", output);
  if (status) url.searchParams.set("status", status);
  return url;
}

function authHeaders(): Record<string, string> {
  const token = engineToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

function timeoutMs(): number {
  return Number(process.env.MSW_ENGINE_HEALTH_TIMEOUT_MS ?? 5000);
}

export async function getEngineStatus(): Promise<EngineStatusView> {
  const url = apiUrl("/api/status");
  if (!url) {
    return {
      status: EMPTY_STATUS,
      error:
        "MSW_ENGINE_URL is not configured, so no evidence can be read. Set it to the engine service and set MSW_OUTPUT_DIR to the conversion output directory.",
    };
  }

  try {
    const response = await fetch(url, {
      headers: authHeaders(),
      cache: "no-store",
      signal: AbortSignal.timeout(timeoutMs()),
    });

    if (!response.ok) {
      const body = (await response.json().catch(() => null)) as { error?: string } | null;
      return {
        status: EMPTY_STATUS,
        error: body?.error ?? `The engine returned ${response.status} from ${url.pathname}.`,
      };
    }

    return { status: (await response.json()) as ProjectStatus, error: null };
  } catch (error) {
    return {
      status: EMPTY_STATUS,
      error: `Could not reach the engine at ${url.origin}: ${
        error instanceof Error ? error.message : "unknown error"
      }.`,
    };
  }
}

export type EvidenceName = "report" | "roundtrip" | "tests" | "conversion-report";

export interface EvidenceResult {
  ok: boolean;
  text: string | null;
  error: string | null;
}

export async function getEvidence(name: EvidenceName): Promise<EvidenceResult> {
  const url = apiUrl(`/api/evidence/${name}`);
  if (!url) return { ok: false, text: null, error: "MSW_ENGINE_URL is not configured." };

  try {
    const response = await fetch(url, {
      headers: authHeaders(),
      cache: "no-store",
      signal: AbortSignal.timeout(timeoutMs()),
    });

    if (!response.ok) {
      const body = (await response.json().catch(() => null)) as { error?: string } | null;
      return { ok: false, text: null, error: body?.error ?? `The engine returned ${response.status}.` };
    }

    return { ok: true, text: await response.text(), error: null };
  } catch (error) {
    return {
      ok: false,
      text: null,
      error: `Could not reach the engine: ${error instanceof Error ? error.message : "unknown error"}.`,
    };
  }
}

export const STATUS_LABELS: Record<EvidenceStatus, string> = {
  verifiedInTia: "VERIFIED IN TIA",
  unitTestOnly: "UNIT-TEST ONLY",
  unverified: "UNVERIFIED",
  notStarted: "NOT STARTED",
  failed: "FAILED",
};