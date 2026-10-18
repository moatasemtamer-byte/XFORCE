# Nexus redesign — build notes

Implements `DESIGN_PLAN.md` (Pass 1). Pass 2 done for the shell and the two live
screens (Dashboard, Convert). Types, wording and evidence logic in `src/lib/engine.ts`
are untouched; only presentation changed.

## What shipped

- **Tokens / theme** — `nx-*` tokens in `globals.css` `@theme`, plus a `.nx-app`
  scoped light base (system stack, light colour-scheme, heading/border re-point).
  The marketing site and the SaaS dashboard keep Geist and the dark palette; nothing
  outside `.nx-app` changed.
- **Shell** — `src/components/nx/`:
  `brand.tsx` (Nexus wordmark + Xforce lockup), `screen-title.tsx`, `sidebar.tsx`
  (3px neon rail, rung markers, active = GREEN-100), `footer-band.tsx`
  (opaque black, 3px neon top edge, warning icon, live counts, not dismissible),
  `splash.tsx` (`NxSplash`, once per session via `sessionStorage`).
- **Shared** — `primitives.tsx` (panel/title/stat/button/field/empty),
  `contact-icons.tsx` (closed/half/open/broken), `status-chip.tsx` (icon + word,
  never colour-only), `convert-stations.tsx` (Parse→Validate→Map→Generate→Report wire,
  animates only while a run is in flight).
- **Dashboard** (`engine/page.tsx`) — phases ladder hero; a "Signals" strip replaces
  the 4-up card row; conversion summary; grouped evidence items; round-trip table;
  known issues.
- **Convert** (`engine/convert/page.tsx`, `components/engine/convert-form.tsx`) —
  station wire + nx form. Form still posts `options`/`notes` to the existing proxy.
- Removed the now-unused dark `components/engine/status-badge.tsx` and `footer-banner.tsx`.

## Decisions / deviations

- **Nexus wordmark** rendered from the supplied `public/brand/nexus-logo.png`
  (269×88, transparent). No interim text label needed.
- **Icons hand-rolled inline SVGs** — no `lucide-react` dependency added (per request).
- **No per-stage progress is invented.** The engine returns a single final result, so
  the Convert wire is only ever "live" or "not live"; stations never show fake steps.
- **No splash flash for returning sessions**: the server renders nothing, the client
  reads `sessionStorage` once. There is an `react-hooks/set-state-in-effect` disable on
  that single, intentional one-time gate.
- **Sidebar items beyond Dashboard/Convert are marked "soon"** and are non-interactive,
  so the rail shows the full information architecture without routing to empty pages.

## Verified

- `npx tsc --noEmit` clean, `npm run lint` clean, `npm run build` clean
  (`/engine`, `/engine/convert` dynamic).
- Live: `/engine` 200 with `VERIFIED IN TIA`, `UNIT-TEST ONLY`, `UNVERIFIED`,
  `NOT STARTED` and the safety band; `/engine/convert` 200 with the five stations.

## Screenshots

- Before: `docs/design/before/{engine-dashboard,engine-convert,landing}.png`
- After: `docs/design/after/{engine-dashboard,engine-convert}.png`
  (headless Edge 1440px).

## Still to do

- Remaining screens from `GUI_PROMPT_IMPROVED.md`: Report viewer, Library call map,
  Address audit, Names & identifiers, Goldens & capacity, TIA connection + round trip
  (sidebar entries already reserved).
- Optional: mobile sidebar drawer; broader contrast sweep once the remaining tables
  (address audit, 2,381 rows) exist.