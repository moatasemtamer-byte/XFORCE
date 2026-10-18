"use client";

import { usePathname } from "next/navigation";

const TITLES: Record<string, string> = {
  "/engine": "Dashboard",
  "/engine/convert": "Convert",
  "/engine/report": "Report",
  "/engine/library": "Library call map",
  "/engine/names": "Names & identifiers",
  "/engine/goldens": "Goldens & capacity",
  "/engine/tia": "TIA connection",
};

export function NxScreenTitle() {
  const pathname = usePathname();
  const title = TITLES[pathname] ?? "Evidence workbench";
  return <span className="truncate text-sm font-semibold text-nx-ink">{title}</span>;
}