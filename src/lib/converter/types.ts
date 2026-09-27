export interface ConversionOptions {
  targetFormat: string;
  includeNetlist: boolean;
  includeBom: boolean;
  includeRungsReport: boolean;
  sheetName: string | null;
  notes: string | null;
}

export interface ConversionInput {
  jobId: string;
  filename: string;
  mimeType: string;
  bytes: Buffer;
  licenseKey: string | null;
  userEmail: string;
}

export interface ConversionArtifact {
  name: string;
  bytes: Buffer;
  mimeType: string;
  kind: "primary" | "report" | "netlist" | "bom" | "preview";
}

export interface ConversionOutcome {
  artifacts: ConversionArtifact[];
  warnings: string[];
  summary: Record<string, unknown>;
  engineVersion: string;
}

export interface EngineStatus {
  ready: boolean;
  adapterId: string;
  label: string;
  version: string;
  detail: string;
}

export class EngineUnavailableError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = "EngineUnavailableError";
    this.code = code;
  }
}

export interface ConverterAdapter {
  readonly id: string;
  readonly label: string;
  readonly version: string;
  status(): Promise<EngineStatus>;
  available(): Promise<boolean>;
  convert(input: ConversionInput, options: ConversionOptions): Promise<ConversionOutcome>;
}

export const DEFAULT_OPTIONS: ConversionOptions = {
  targetFormat: "msw",
  includeNetlist: true,
  includeBom: false,
  includeRungsReport: true,
  sheetName: null,
  notes: null,
};
