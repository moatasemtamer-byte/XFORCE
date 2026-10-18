# Nexus GUI redesign — Pass 1 plan

Companion to `GUI_PROMPT_IMPROVED.md` (functional) and `GUI_REDESIGN_NEXUS_PROMPT_v2.md`
(visual). This document is **plan only**. It follows section 8.2 of the v2 prompt and is
reviewed against section 3 ("what not to copy") before any code is written.

## 0. Identity resolution

- **App name:** Nexus. **Maker / main brand:** Xforce (the site is XFORCEMSW; the logo lockup
  reads "Xforce Engineering Solutions"; all the same company).
- Top bar: `NEXUS` wordmark, 1px divider, Xforce lockup, then screen title.
- The tagline "From Any Platform… To Any Platform" appears on splash and About only. The
  working UI says it converts Triconex to TIA V16 and claims nothing more.
- Status words never change: `VERIFIED IN TIA`, `UNIT-TEST ONLY`, `UNVERIFIED`, `NOT STARTED`, `FAILED`.

### Assets
- Staged into repo: `public/brand/xforce-logo.png` (1900×301, white bg, no alpha) and
  `public/brand/splash-bg.png` (1536×1024). Existing `public/brand/xforcemsw-logo.png`
  (1983×793) is the same artwork family.
- **Nexus wordmark: still only inside `splash-bg.png`.** Interim: render "Nexus" as a plain
  uppercase text label in the top bar (not a redrawn logo), flagged temporary in `DESIGN_NOTES.md`.
  Need a transparent PNG/SVG to replace it.

## 1. Tokens (Tailwind v4 `@theme` additions in `src/app/globals.css`)

Named `nx-*` so they do not collide with the existing marketing tokens (`brand-*`, `paper*`, `ink*`).

| Token | Value | Use |
|---|---|---|
| `--color-nx-paper` | `#FAFAFA` | page background |
| `--color-nx-white` | `#FFFFFF` | panels, sidebar, tables |
| `--color-nx-ink` | `#0A0A0A` | text, black buttons, console, footer band |
| `--color-nx-body` | `#4B5563` | secondary text |
| `--color-nx-hint` | `#6B7280` | hints/captions |
| `--color-nx-line` | `#E5E7EB` | 1px rules |
| `--color-nx-line-strong` | `#D1D5DB` | table header rule, input borders |
| `--color-nx-neon` | `#10FF00` | brand green: rails, bars, markers, fills w/ black text. Never text on white |
| `--color-nx-green-700` | `#0A7D12` | green text/links/icons on white |
| `--color-nx-green-100` | `#D9FFD5` | active nav / selected row |
| `--color-nx-green-50` | `#F1FFEF` | tinted bands, table header, empty states |
| `--color-nx-console` | `#111111` | console body (border `#1F2937`) |

Status chip tokens (used **only** inside chips, row left-rules, capacity gauge):

| Status | bg | fg |
|---|---|---|
| VERIFIED IN TIA | `#10FF00` (nx-neon) | `#0A0A0A` |
| UNIT-TEST ONLY | `#FFF1D0` | `#8A5300` |
| UNVERIFIED | `#FFE6D8` | `#B5400C` |
| NOT STARTED | `#EEF1F3` | `#52626F` |
| FAILED | `#FCE4E1` | `#A8271C` |

**Font:** the doc mandates the landing page's system stack. Scope it to the app shell via a
`.nx-app { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif }`
wrapper so the marketing site keeps its current fonts. Monospace only for logs/addresses/SCL.

## 2. Wireframes

### Shell
```
+--------------------------------------------------------------------------+
| [NEXUS] | [ XFORCE logo ]   Screen title                                 |  white bar, 1px --nx-line bottom, no blur/shadow
+----------+---------------------------------------------------------------+
| || Dash  |                                                               |
| || Conv  |   page content on --nx-paper                                  |
| || ...   |                                                               |
+----------+---------------------------------------------------------------+
| ^ 3px neon top edge                                                      |
| [warning]  Safety-critical TMR output — verify before use   u:2 uto:4 cw:1 ki:0 |  solid --nx-ink band, white text, ~44px, not dismissible
+--------------------------------------------------------------------------+
```
Sidebar `||` = 3px neon rail at the left edge; each item is a rung with a short horizontal
wire and a neon marker where the wire meets the rail. Active item: `--nx-green-100` bg, `--nx-ink` text.

### Phases ladder (Dashboard hero)
```
  (left rail)                                    (right rail)
   |---[contact]-- 0  Infrastructure (UDTs+GlobalDB+Tags)   [UNVERIFIED] --|
   |===[closed ]== 1  Simplest ST block                      [VERIFIED IN TIA] ==|   neon
   |---[contact]-- 2  ST blocks with IEC library calls       [UNIT-TEST ONLY]-|
   ...
```
Energized (neon, closed contact) only when evidence = VERIFIED IN TIA; else gray, open contact.
A neon & white pixel on the left of each rung row = power.

### Convert (stations on a wire)
```
 PARSE ----[]---- VALIDATE ----[]---- MAP ----[]---- GENERATE ----[]---- REPORT
   [terminal]        [terminal]     [terminal]      [terminal]       [terminal]
   signal travels while running; completed stations show their result count
 +--------------------------------------------------------------------------+
 | > engine log line                                                     _  |  black console, fixed height, own scroll
 +--------------------------------------------------------------------------+
```

### Status chip
```
(neon bg) [contact✓] VERIFIED IN TIA     (amber bg) [half-contact] UNIT-TEST ONLY ...
```
Icon + word always present; never color alone.

### Table system (Report viewer / Address audit / Names / Library map)
```
+-- --nx-green-50 header band, sticky -------------------------------------+
| Kind | Name               | ... | Status          |
+------+--------------------+-----+-----------------+   row rules, no zebra
|  ╎ Block| PumpCtrl         |     | [chip]          |   ╎ = colored left rule (collision/type-conflict)
+--------------------------------------------------------------------------+
```
Numbers right-aligned, tabular. Address audit virtualized (2,381 rows).

## 3. Components to build / restyle

**New**
- `src/components/nx/app-shell.tsx` — top bar + sidebar rail + content frame.
- `src/components/nx/sidebar.tsx` (client, `usePathname`) — rail + rungs + active state.
- `src/components/nx/footer-band.tsx` — opaque black band, neon top edge, warning icon, counts.
- `src/components/nx/status-chip.tsx` — the five chips **with contact icons** (replaces the color-only `engine/status-badge.tsx`).
- `src/components/nx/contact-icons.tsx` — closed/half/open/broken contact SVGs.
- `src/components/nx/ladder-phases.tsx` — the phases ladder hero.
- `src/components/nx/console.tsx` — black console panel (model-on `landing/engine-console.tsx`).
- `src/components/nx/table.tsx` — the table system.
- `src/components/nx/panel.tsx`, `button.tsx`, `empty-state.tsx` — plain 2px white rectangles, black primary buttons.
- `src/components/splash/splash-screen.tsx` + `splash-gate.tsx` — from supplied `SplashScreen.jsx`/`.css`, styles folded into `globals.css`, shown once per session, mounted in the app shell.

**Restyle / replace theme**
- `src/app/engine/layout.tsx` — light `nx` shell (currently dark `ui.tsx`).
- `src/app/engine/page.tsx` — Dashboard (currently dark cards).
- `src/app/engine/convert/page.tsx` + `src/components/engine/convert-form.tsx` — Convert (currently dark form).
- Retire `src/components/engine/status-badge.tsx` and `footer-banner.tsx` usage in favor of `nx/*`.

**Do not touch:** `engine/**` (C#), `src/lib/engine.ts` (fetch/types), status logic/wording, tests.

## 4. Self-critique against section 3 (before writing any style)

First draft of the shell had `panel-raised` cards, a rounded pill for the app name, and a
tracked-uppercase eyebrow above each panel — all direct hits on the do-not-copy list. Revisions:

1. Removed all shadows and 14px radii; panels are 2px-cornered white rectangles with 1px `--nx-line`.
2. Dropped the pill and every eyebrow; the only uppercase text is the page title.
3. Replaced the 4-up identical card row with the **phases ladder** as the hero, plus counts as
   bare numerals separated by hairlines (no boxes).
4. Removed scroll-reveal and backdrop-blur from the shell; the only motion is the signal
   during an active conversion and the one-time splash.
5. Status is a chip **with a contact icon**, never color alone.
6. The one neon-with-text-and-check element is the `VERIFIED IN TIA` chip only; primary buttons are black.

## 5. Order of build (Pass 2)

a. Tokens + `.nx-app` base. b. Shell (top bar, sidebar, footer band). c. Shared (chip+icons,
buttons, table, panel, console). d. Dashboard ladder + Convert wire. e. Remaining screens.
Screenshots to `docs\design\after\` after each step; contrast script + lint each step.

## 6. Open questions
1. **Nexus wordmark** — provide transparent PNG/SVG (or approve the text-label interim).
2. **`lucide-react` is not installed** — approve adding it for the 1.5-stroke icons, or keep
   hand-rolled inline SVGs (only the contact icons are truly custom)?
3. Confirm the app shell is **web-only** (no WPF) — yes per inspection.