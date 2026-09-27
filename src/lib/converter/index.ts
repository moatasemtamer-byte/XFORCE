import { HttpEngineAdapter } from "./http";
import { StubAdapter } from "./stub";
import { ConverterAdapter, EngineStatus } from "./types";

export * from "./types";
export { HttpEngineAdapter } from "./http";
export { StubAdapter } from "./stub";

let cached: ConverterAdapter | null = null;

export function getAdapter(): ConverterAdapter {
  if (cached) return cached;
  cached = HttpEngineAdapter.fromEnv() ?? new StubAdapter();
  return cached;
}

export async function engineStatus(): Promise<EngineStatus> {
  return getAdapter().status();
}
