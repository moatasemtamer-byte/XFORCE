"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";

/**
 * Refreshes the job page while the conversion is still running, so status, progress and
 * outputs appear without the user reloading. Stops as soon as the job settles.
 */
export function JobAutoRefresh({ active }: { active: boolean }) {
  const router = useRouter();

  useEffect(() => {
    if (!active) return;
    const id = window.setInterval(() => router.refresh(), 3000);
    return () => window.clearInterval(id);
  }, [active, router]);

  return null;
}
