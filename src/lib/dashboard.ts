import { engineStatus, type EngineStatus } from "./converter";
import { countJobs } from "./jobs";
import { effectivePlan, listLicenses, monthlyUsage, type PlanDefinition } from "./licensing";
import type { License, User } from "./types";

export interface DashboardContext {
  plan: PlanDefinition;
  licenses: License[];
  usedThisMonth: number;
  remaining: number;
  usagePercent: number;
  counts: { total: number; completed: number; failed: number };
  engine: EngineStatus;
}

export async function getDashboardContext(user: User): Promise<DashboardContext> {
  const licenses = listLicenses(user.id);
  const plan = effectivePlan(user, licenses);
  const usedThisMonth = monthlyUsage(user.id);
  const limit = plan.monthlyConversions;
  const remaining = Number.isFinite(limit) ? Math.max(0, limit - usedThisMonth) : Number.POSITIVE_INFINITY;

  return {
    plan,
    licenses,
    usedThisMonth,
    remaining,
    usagePercent: Number.isFinite(limit) && limit > 0 ? Math.min(100, (usedThisMonth / limit) * 100) : 0,
    counts: countJobs(user.id),
    engine: await engineStatus(),
  };
}
