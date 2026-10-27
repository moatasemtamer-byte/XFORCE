# Ladder pipeline (PDF / LPT) — correctness rules

Turns a ladder PDF or a TriStation ASCII listing (`.lpt` / `.txt`) into TriStation PLCopen XML:

```
PDF/LPT --[circuit-reader]--> equation listing --[BL workbook]--> XML (one program per group) --> merge
```

## Input handling

- A `.pdf` is passed through untouched.
- A `.lpt` / `.txt` is passed **as-is**. `circuit-reader` identifies a PDF by its `%PDF-`
  signature and parses anything else with its `TextListingReader`, so rendering the listing to PDF
  first is unnecessary.
- The old LPT → PDF → re-parse step was removed: it could drop lines that overflowed a page, it
  needed a Windows font, and it re-decoded the file twice. On the reference listing
  (`CATORIZED_.LPT`, 168 KB, 225 rungs) the direct path produces **the identical equation
  listing** as the PDF round trip (294 lines, 0 differences), same dialect, rung and address
  counts.

## Program grouping (`LptProgramMap`)

- `PROGRAM=<name>` is read from the network headers; a header without a tag belongs to the
  preceding program.
- Rows are grouped by name **across the whole listing** (first-appearance order), so a program
  whose networks resume later stays one group.
- If network numbers repeat across programs (two programs both starting at `#1`), grouping falls
  back to **listing order** instead of a number-keyed map, which would collide.
- Non-ASCII bytes decode as strict UTF-8, then Latin-1 (byte-preserving), instead of silently
  becoming U+FFFD.
- On the reference listing this reproduces the six programs and their row counts exactly:
  `COMMON 36`, `DIAGNOSTICS 28`, `SYSTEM140_141 181`, `SYSTEM150_184 8`, `SYSTEM192 9`,
  `SYSTEM198 30`.

## Quality gates (the run fails instead of emitting a bad project)

- Every group must produce **exactly one** non-empty program POU; otherwise the run fails with
  the group name.
- POU names are sanitised and made unique (`A-B` and `A_B` cannot collide).
- Renames are applied **document-wide**, so task/resource references never keep the old name.
- Repeated POU definitions that arrive with the BL template are removed (a TriStation export can
  carry a function block twice; keeping both makes the editor duplicate the body).
- After the merge: no duplicate POU name, and every requested program is present exactly once.
- Placements whose name TriStation cannot store (empty, `10`, `dir/…`, `L%4102`) are reported with
  a count and a sample in the build report — they are dropped by the importer, so the loss is
  visible rather than silent.

## Measured

Reference run (`CATORIZED_.LPT` → XML): **12 s** total — circuit-reader 0.5 s, six BL runs
1.1–3.0 s each, merge instantaneous. Output: 8.5 MB XML, 6 generated programs + template
programs, 225 rungs, 361 addresses.

## Stage timings

`ladder_report.txt` now records `circuit-reader: Xs`, `equations: N row(s)`, per-group
`in Xs`, `template: removed …`, and `project: N program POU(s)`.
