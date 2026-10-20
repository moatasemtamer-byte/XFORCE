import { FEATURES, type FeatureDefinition } from "@/lib/licensing";
import type { LicenseFeature } from "@/lib/types";

const GROUPS: FeatureDefinition["group"][] = ["Convert", "Source files", "Artifacts", "Tools"];

/**
 * Ticked capability checkboxes, one fieldset per group. Posts repeated `features`
 * values; the action validates each with `isFeature`. `name="featuresPresent"`
 * marks that this form owns the feature list, so terms edits elsewhere (seat
 * counts, status) never overwrite it.
 */
export function FeaturePicker({
  selected,
  idPrefix,
}: {
  selected: LicenseFeature[];
  idPrefix: string;
}) {
  const ticked = new Set(selected);

  return (
    <fieldset className="space-y-3">
      <legend className="text-[11px] font-medium uppercase tracking-[0.14em] text-mist-500">
        Capabilities
      </legend>
      <input type="hidden" name="featuresPresent" value="1" />
      <div className="grid gap-3 sm:grid-cols-2">
        {GROUPS.map((group) => {
          const items = FEATURES.filter((feature) => feature.group === group);
          if (items.length === 0) return null;
          return (
            <div key={group} className="rounded-lg border border-ink-700 bg-ink-900/40 p-3">
              <p className="text-[11px] font-semibold text-mist-300">{group}</p>
              <ul className="mt-2 space-y-2">
                {items.map((feature) => (
                  <li key={feature.id}>
                    <label
                      htmlFor={`${idPrefix}-${feature.id}`}
                      className="flex cursor-pointer items-start gap-2 text-[11px] leading-snug text-mist-300"
                    >
                      <input
                        id={`${idPrefix}-${feature.id}`}
                        type="checkbox"
                        name="features"
                        value={feature.id}
                        defaultChecked={ticked.has(feature.id)}
                        className="mt-0.5 h-3.5 w-3.5 shrink-0 accent-signal-400"
                      />
                      <span>
                        <span className="text-mist-200">{feature.label}</span>
                        <span className="block text-mist-500">{feature.description}</span>
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
            </div>
          );
        })}
      </div>
    </fieldset>
  );
}
