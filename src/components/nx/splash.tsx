"use client";

import { useEffect, useState } from "react";

const SEEN_KEY = "nx-splash-seen";

/**
 * One-time splash, shown once per browser session. The artwork is the supplied
 * /brand/splash-bg.png served unmodified; only the loader animates.
 */
export function NxSplash({ minDuration = 1700 }: { minDuration?: number }) {
  const [state, setState] = useState<"hidden" | "visible" | "leaving">("hidden");
  const [progress, setProgress] = useState(0);

  // The server renders nothing; the gate is decided once on the client. Nothing
  // shows for a returning session, so there is no hydration mismatch.
  useEffect(() => {
    let seen = false;
    try {
      seen = sessionStorage.getItem(SEEN_KEY) === "1";
    } catch {
      seen = false;
    }
    // eslint-disable-next-line react-hooks/set-state-in-effect -- one-time client-only session gate
    setState(seen ? "hidden" : "visible");
  }, []);

  useEffect(() => {
    if (state !== "visible") return undefined;
    const start = performance.now();
    const id = window.setInterval(() => {
      const t = Math.min(1, (performance.now() - start) / minDuration);
      setProgress((1 - Math.pow(1 - t, 3)) * 100);
      if (t >= 1) {
        window.clearInterval(id);
        try {
          sessionStorage.setItem(SEEN_KEY, "1");
        } catch {
          /* storage unavailable; splash simply shows again next time */
        }
        setState("leaving");
        window.setTimeout(() => setState("hidden"), 500);
      }
    }, 60);
    return () => window.clearInterval(id);
  }, [state, minDuration]);

  if (state === "hidden") return null;

  const pct = Math.round(progress);

  return (
    <div
      className={`nx-splash${state === "leaving" ? " nx-splash--leaving" : ""}`}
      role="status"
      aria-live="polite"
      aria-label="Nexus is loading"
    >
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img className="nx-splash__backdrop" src="/brand/splash-bg.png" alt="" aria-hidden="true" />
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        className="nx-splash__art"
        src="/brand/splash-bg.png"
        alt="Nexus — Xforce Engineering Solutions. PLC migration and logic conversion."
      />
      <div className="nx-splash__loader">
        <div
          className="nx-splash__track"
          role="progressbar"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={pct}
          aria-label="Loading progress"
        >
          <div className="nx-splash__fill" style={{ width: `${progress}%` }} />
        </div>
        <div className="nx-splash__meta">
          <span>Loading evidence workbench…</span>
          <span className="nx-splash__pct">{pct}%</span>
        </div>
      </div>
    </div>
  );
}