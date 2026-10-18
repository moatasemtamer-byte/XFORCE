import { engineStatus, type EngineStatus } from "./converter";
import { countJobs } from "./jobs";
import {
  effectivePlan,
  listLicenses,
  listSeatedLicenses,
  monthlyUsage,
  type PlanDefinition,
} from "./licensing";
import type { License, User } from "./types";

export interface DashboardContext {
  plan: PlanDefinition;
  licenses: License[];
  /** Licenses issued to this account, plus ones it merely holds a seat on. */
  usableLicenses: License[];
  usedThisMonth: number;
  remaining: number;
  usagePercent: number;
  counts: { total: number; completed: number; failed: number };
  engine: EngineStatus;
}

export async function getDashboardContext(user: User): Promise<DashboardContext> {
  const licenses = listLicenses(user.id);
  // A seat on someone else's license still carries that license's plan, otherwise
  // holding a shared seat would entitle the holder to nothing.
  const usableLicenses = [...licenses, ...listSeatedLicenses(user.id)];
  const plan = effectivePlan(user, usableLicenses);
  const usedThisMonth = monthlyUsage(user.id);
  const limit = plan.monthlyConversions;
  const remaining = Number.isFinite(limit) ? Math.max(0, limit - usedThisMonth) : Number.POSITIVE_INFINITY;

  return {
    plan,
    licenses,
    usableLicenses,
    usedThisMonth,
    remaining,
    usagePercent: Number.isFinite(limit) && limit > 0 ? Math.min(100, (usedThisMonth / limit) * 100) : 0,
    counts: countJobs(user.id),
    engine: await engineStatus(),
  };
}
