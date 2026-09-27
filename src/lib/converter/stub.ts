import {
  ConversionOutcome,
  ConverterAdapter,
  EngineStatus,
  EngineUnavailableError,
} from "./types";

export class StubAdapter implements ConverterAdapter {
  readonly id = "stub";
  readonly label = "Bundled stub (engine not connected)";
  readonly version = "0.0.0-stub";

  async available(): Promise<boolean> {
    return false;
  }

  async status(): Promise<EngineStatus> {
    return {
      ready: false,
      adapterId: this.id,
      label: this.label,
      version: this.version,
      detail:
        "No conversion engine is connected yet. Jobs are accepted and queued, but the conversion step is a placeholder until MSW_ENGINE_URL points at your engine service.",
    };
  }

  async convert(): Promise<ConversionOutcome> {
    throw new EngineUnavailableError(
      "engine_not_connected",
      "The MSW conversion engine is not connected. Set MSW_ENGINE_URL (and optionally MSW_ENGINE_TOKEN) to activate conversions.",
    );
  }
}
