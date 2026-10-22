# XML -> PT2X builder — `pt2conv buildxml` / `genfbd`

Reverse-engineered rules for turning TriStation PLCopen XML (`SULFUR1__2.xml`) back
into a `.pt2x` container that matches a real TriStation 5.8.0 export
(`SULFUR1__2.PT2X`). This is the inverse of `frompt2`, so the same wire format is
reused in reverse. Sections marked **(verified)** were checked byte/attribute-for-
attribute against the reference extract at
`%TEMP%\opencode\good_pt2x\Current\Elements\<pou>\<id>.FBD`.

## 1. Container (CFB/OLE) **(verified)**

The reference container is **OLE/CFB v4 with 4096-byte sectors**.

| Header field (offset) | Reference value | Ours (old) |
| --- | --- | --- |
| MinorVersion (24) | `0x003E` (62) | `0x003E` |
| MajorVersion (26) | `0x0004` (v4) | `0x0003` (v3) |
| ByteOrder (28) | `0xFFFE` | ok |
| SectorShift (30) | `12` (4096-byte sectors) | `9` (512) |
| MiniSectorShift (32) | `6` (64-byte mini sectors) | ok |
| NumDirSectors (40) | `62` (v4 REQUIRES this) | `0` (v3) |
| MiniStreamCutoff (56) | `4096` | ok |

Rules: v4 uses 4096-byte sectors; `NumDirSectors = ceil(numDirEntries*128/4096)`;
FAT entries per sector = 1024; DIFAT entries per sector = 1023. The directory
red-black tree ordering (left/right/child/color) is a deterministic name-ordered BST
and matches ours once the sector size / counts are v4.

## 2. FBD stream is a serialization of the XML `<FBD>` body **(verified)**

`Current/Elements/<pou>/<hexid>.FBD` is a UTF-8 XML document, root
`<SerializableClass ClassType="CFBDDocXml">`. It is **not** a compiled netlist: it is
the *diagram* (sheets, positioned objects, wires) serialized from the XML `<body><FBD>`
with a fixed coordinate transform. Everything needed is already in the XML.

### 2.1 Header attributes -> XML sources (verified on FB04_DI/CHANGEOVER_SWITCH/P01_FB)

| FBD attr | Source |
| --- | --- |
| `pouName` / `pouType` / `pouVersion` | `pou@name` / `pou@pouType` (lower-cased) / `pou@version` |
| `dateTime` | export timestamp (e.g. `10/20/2026 7:41:15 PM`) |
| `sheetTemplate` / `drawingSize` / `hZoneCount` / `vZoneCount` | `ts:SHEETS@Template` / derive sheet size from `Width,Height` / `Width = hZoneCount*20+2` / `Height = vZoneCount*25+2` |
| `attributes` | constant `1204` |
| `lastUniqueID` | max `localId` in the emitted stream (e.g. 40 / 44) |
| `D0..D9` | sheet macros? (constant `""` in samples) |
| `drawingNumber` / `drawingTitle` | `ts:POU_EXT` macros `DRAWING_NUMBER` / `DRAWING_TITLE` |
| `approvedName` / `approvedTime` / `rev` | `ts:POU_EXT` macros `APPROVED_NAME`/`APPROVED_TIME`/`REV` |
| `extOptFlags` | `513` for program-with-only-faceplate (`P01_FB`); `3` for FB with logic |
| `cemEventInputs` / `cemActionOutputs` | `0` |

### 2.2 Coordinate transform **(verified)**

For every positioned object:

```
posX = xml_x * 10 + 40
posY = xml_y * 10 + 40
width  = xml_width  * 10
height = xml_height * 10
```

Examples (FB04_DI): comment `x=0 y=2 w=20 h=4` -> `posX=40 posY=60 w=200 h=40`;
block `x=121 y=19 w=10 h=8` -> `posX=1250 posY=230 w=100 h=80`;
wire point `(120,25)` -> `(1240,290)`.

### 2.3 Declarations (`Collection Name="declarations"`, CTS2Vars)

Order = XML interface order: `inputVars`, local `inOutVars`, `localVars`, `outputVars`
(exact reference order: the variable declarations appear as
inputs, then inOuts, then locals, then outputs as they appear in `<interface>`).

| attr | Source |
| --- | --- |
| `name` | variable name |
| `dataType` | `<type>` leaf (`BOOL`) or `<derived name="RS">` |
| `initVal` | `<initialValue><simpleValue value="">` |
| `desc` | `<documentation>` CDATA |
| `varVTType` | `0` input, `1` output, `3` derived FB instance, `5` global program var |
| `refCount` | number of times the variable is **read** (source pin) — inputs/output-seen = 2 in the reference for used vars |
| `fbRefCount` | number of times used as an FB **instance** (derived type) |
| `flags` | `0` normal; `33554434` (`0x2000002`) for derived FB instances |
| `writes` | `0` |

### 2.4 Snapshots (`Collection Name="snapshots"`, CPRJLogElemSnapshot)

One `<Class>` per **block type** used (e.g. `R_TRIG`, `RS`, `AND`), with the FB's
native pin declarations copied from the library element. Verified fields: `name`,
`dogTag`, `type`, `subType`, `instanceCount`, `commentCount`, `extOptFlags`, an
`Embed="eventConnectionsChanged"` child with `eventType/eventId/date/user`, and a
`declarations` collection of the block's pins (name/dataType/varVTType).

### 2.5 Sheets and the duplication rule (KEY) **(verified)**

The stream always contains the diagram **twice**: a "primary" copy then a "secondary"
copy, both inside the same `CFBDSheetXml` sheet(s). This is NOT control-flow
duplication — it is how TriStation stores the diagram (an editor pass + a build pass).
Evidence:

- FB04_DI: comments `4..11` then `24..31` (+20), blocks `2,3` then `22,23` (+20),
  wires `15,16,17,20` then `35,36,37,40` (+20).
- CHANGEOVER_SWITCH: comments `3..7` then `25..29` (+22), funcs `1,2` then `23,24` (+22),
  wires `12,19,18,20,15,22` then `34,41,40,42,37,44` (+22).

The **copy-2 offset** equals the highest primary localId across the whole POU (all
sheets). So:

```
secondary_localId = primary_localId + PRIMARY_SPAN
PRIMARY_SPAN      = max(primary localId over all sheets)
lastUniqueID      = 2 * PRIMARY_SPAN   (usually; see below)
```

Verified: FB04_DI primary `1..20` -> secondary `21..40` (offset 20, lastUniqueID 40);
CHANGEOVER_SWITCH offset 22 (lastUniqueID 44); TEMPLATES offset 96 (lastUniqueID 192).
`lastUniqueID` can be `2*PRIMARY_SPAN + 1` when edit history reserved a stray id
(AI_COND: offset 308, lastUniqueID 617 = 2*308 + 1).

**`PRIMARY_SPAN` and the exact primary ids are NOT generically derivable (verified).**
The primary id sequence contains **gaps left by TriStation edit history** (objects the
user created then deleted). Examples: FB04_DI primary ids are
`1..17, 20` (18 missing) with secondary `21..37, 40` (38,39 missing); AI_COND has a
stray reserved id making `lastUniqueID = 2*offset + 1`. Reconstructing the diagram from
the XML cannot know those gaps, so our builder emits **contiguous** primary ids and the
matching `2*max` `lastUniqueID`. This is an **accepted, documented deviation** (the same
class of deviation documented for `dateTime`).

**`localId` allocation for wires:** each `<connection>` in the XML produces a
`CDGMWireXml` whose `localId` is allocated in XML document order. The wire is emitted
**immediately after the object that owns its `connectionPointIn`** (the target/consumer
of the wire), not after the source. The `connectId` of the wire's `Embed="input"` is the
owning object's localId; `Embed="output"` connectId is the `refLocalId` (source object).

### 2.5a Displayable emission order is authoring z-order, NOT derivable (verified)

Within a sheet the reference lists displayables in TriStation's internal **z-order /
creation order**, not in any XML-derivable order. Two independent symptoms:

1. **Ids have edit-history gaps** (see 2.5 above): the id sequence is not `1..n`.
2. **The secondary copy is interleaved per-sheet, not appended per-POU.** For some
   sheets the secondary copy's *comments* are prepended ahead of the primary copy's
   objects, while the secondary copy's *blocks/wires* stay at the end. Verified
   examples (equal bodies in the XML; `copies=2` byte-identical):

   - TEMPLATES sheet1 — GOOD:
     `Scomments(103..99) Pcomments(7..3) Pblock(2) PInOut(8,9) Sblock(98) SInOut(104,105)`.
     OURS: `Pcomments Pblock PInOut Scomments Sblock SInOut`.
   - AI_COND sheet0 — GOOD: `Scomments(324..320) Pcomments(16..12) divider(1)
     Pcomments(17,18) Pelements... Scomments(325,326)...`.

Because the XML stores the diagram with no `sequence`/creation metadata and both `<pou>`
copies are byte-identical, the per-sheet interleave cannot be reproduced generically.
Our builder emits primary-then-secondary per POU. This is an **accepted deviation**
(affects AI_COND, TEMPLATES, SeimensLogic; CHANGEOVER_SWITCH, OUTPUT_TEST, FB04_DI,
P01_FB already match).

**Dominant pattern and exceptions (measured on all sheets).** Comments are emitted as a
contiguous block at the front of each sheet, usually as
`[secondary-copy comments, id DESC][primary-copy comments, id DESC]`
(matches CHANGEOVER_SWITCH, TEMPLATES s1, and ~130 SeimensLogic sheets). But the rule is
**not uniform**, so it cannot be applied blindly:

- FB04_DI sheet1 = `[primary comments, id ASC][secondary comments, id ASC]` (exactly
  inverted) — and FB04_DI is currently byte-identical, so reordering would regress it.
- SeimensLogic s6 = `85,86,87,88,89` DESC then a trailing straggler `90`.
- AI_COND sheets interleave a `networkDivider` between comment runs
  (`[Scomments][Pcomments][divider][Pcomments 17,18]...`).
- OUTPUT_TEST has no comments: `[primary objects][secondary objects]`.

Given a single uniform key (id asc/desc, x asc/desc, y, P-first/S-first) fails on at
least one sheet of one POU, and the existing byte-identical POUs must not regress, the
emission order is left as primary-then-secondary (**authoring z-order, unrecoverable**).

### 2.5b Wire waypoints and tap-junction decomposition (verified)

For every wire the `positions` list is the XML `<connection>` `<position>` list
transformed by 2.2 (`10*v+40`) and, for a plain connection, **reversed** relative to the
XML order (the .pt2x lists source->sink; the XML lists sink->source). Verified exact on
FB04_DI (8 wires), TEMPLATES (72), SeimensLogic (1116): 100% match.

Where two connections share geometry (a **tap/merge net**), the reference re-decomposes
the net using its orthogonal router, which inserts junction vertices and can overlap
segments. Example (CHANGEOVER_SWITCH):
XML conn `770,290;540,290;540,560;200,560` + conn `770,560;200,560` (the second passes
through `540,560`, so the two **overlap** on `540,560-200,560`). Reference emits
`770,290;540,290;540,560` + `200,560;540,560;770,560` (junction `540,560` inserted into
the second). Our generator groups by **source pin** (`FbdGenerator.cs` BuildPlan steps
2-3, `IsBranch`/`Parent`/`Vn`) and produces an **electrically equivalent but
geometrically different** split (`200,560;540,560;540,290;770,290` + `770,560`).

The authoritative model is the inverse walk in `FromPt2.ResolveConn`
(`FromPt2.cs:1639-1778`): start at a sink pin, walk upstream through tap endpoints
(an endpoint whose `connectId` is another wire), choosing the source end by pin role
(`ChooseSourceEnd`/`RoleOf`: output pin = source, input = sink, var kind, else x). The
XML has no wire->wire links, so the tap graph can only be inferred from shared geometry,
and the exact router output (junction insertion + overlap resolution) is
**not generically reproducible** -> accepted deviation for tap-merge nets. The result is
electrically identical and is expected to load/normalise correctly in TriStation, but the
byte-level `positions` differ.

### 2.5c Wire-count surplus: duplicate/overlapping connections (verified)

Per-net wire counts (good vs ours), transformed `10*v+40`, grouped by shared endpoints:

| POU | good wires | our wires |
|-----|-----------:|----------:|
| FB04_DI | 8 | 8 |
| TEMPLATES | 72 | 72 |
| OUTPUT_TEST | 14 | 14 |
| CHANGEOVER_SWITCH | 12 | 12 |
| P01_FB | 0 | 0 |
| AI_COND | 204 | 214 |
| SeimensLogic | 1116 | 1184 |

So the surplus is confined to **AI_COND (+10)** and **SeimensLogic (+68)**. Root cause
(SeimensLogic): the XML `<connection>` list contains many **duplicate / coincident**
connections. Example net `1800,200 ; 480,200` repeats ~90 times in the XML but good emits
far fewer wires; likewise `2830,330 ; 1660,330` (5 identical) and `1440,550 ; 480,550`
(18) map to fewer/zero good wires. Nets where the XML geometry sits on a **pin centre**
(e.g. `950,900 ; 930,900`, `960,520 ; 960,500 ; 640,500`) have `goodWires=0` under pure
endpoint keying, i.e. good merges those into a neighbouring wire.

This is the same class as the verified duplication rule (2.x): the export emits the same
drawn connection multiple times (once per duplicated instance / per source pin) and good
**collapses coincident connections into one wire**. It is a **structural dedup/merge**,
not a routing artifact — distinct from 2.5b. Not yet implemented; our generator emits one
wire per XML connection, hence the surplus.

Two candidate rules to confirm against a `goodWires=0` net before coding:
1. drop byte-identical duplicate connections, then
2. merge connections whose endpoint lies exactly on another connection's segment/pin.

**RESOLVED (measured, 2025):** this is **NOT a separate dedup rule — it is the same
tap/branch decomposition as 2.5b.** Evidence via `wirecmp.ps1` (compare good vs our wire
endpoints `CDGMWireXml/*[@Embed]`):

- all 1253 XML connections are unique by `(refLocalId, owner, formalParameter)` = 0 dupes
- `pinid.ps1`: distinct src = 1178, distinct (sink,pin) = 679, distinct (src,sink,pin) = 1253
- In **our** output many wires share the same **output/source** connectId (e.g. out=2515 x2,
  2519 x2, 2525 x2, 2535 x3 ...). In **good** those collapse to one wire with interior
  branch points. So good emits **one wire per net** (fan-out kept inside the polyline) and
  we emit **one wire per connection**.

Therefore the SeimensLogic +68 (and AI_COND +10) surplus is the branch/tap model of 2.5b;
there is no additional conservative dedup to implement. Fully matching requires the same
router-dependent net decomposition already documented as an accepted deviation.

### 2.5d Net decomposition implementation (in progress)

`FbdGenerator.DecomposeNets` (step 1b in `BuildPlan`, after the raw-wire loop) rebuilds
tap-merge nets:

1. Build an undirected graph over every raw connection path (vertex = point, edge = the
   axis-aligned segment between consecutive points; a vertex introduced by another wire
   passing through a segment via `OnSegment` splits the edge).
2. Junctions = vertices with degree >= 3. A net containing **exactly one junction and three
   arms** is rebuilt; larger fan-out nets keep the export's per-connection wires (this is
   what the editor appears to store for them).
3. Two arms at the junction are merged when they are **collinear and opposite**: cross
   product `inDir.X*outDir.Y - inDir.Y*outDir.X == 0` and dot `inDir·outDir < 0`. Note this
   is a **sign** test, not equality: the two collinear arms are generally of **unequal**
   length (e.g. CHANGEOVER junction `67,38`: up arm length 11, down arm length 12 in raw
   coords), so a `inDir == -outDir` vector test fails.
4. Merged path = the far arm (oriented to end at the junction) followed by the near arm
   with its duplicate junction vertex dropped. `MakeDecomposedWire` reverses the path back
   to source->sink and copies the template's Source/Target; `PathExport` holds raw coords.

Verified geometry (CHANGEOVER_SWITCH, raw coords -> transformed `10*v+40`):

- junction `67,38` (710,420): arms `{67,38;67,27;73,27}` (up), `{67,38;16,38}` (left),
  `{67,38;67,50;73,50}` (down) -> merged vertical `73,50;67,50;67,38;67,27;73,27` =
  `770,540;710,540;710,420;710,310;770,310` (matches good) + stub `67,38;16,38` =
  `710,420;200,420` (matches good).
- junction `50,52` (540,560): merged horizontal `73,52;50,52;16,52` =
  `770,560;540,560;200,560` (good) + arm `50,52;50,25;73,25` (good's is the reverse).

Resulting per-net wire counts after implementation (transformed; `dateTime`-only diffs
ignored for FB04_DI/TEMPLATES/P01_FB which are logic-identical):

| POU | good | ours (before) | ours (now) | delta |
|-----|-----:|--------------:|-----------:|------:|
| FB04_DI | 8 | 8 | 8 | 0 |
| TEMPLATES | 72 | 72 | 72 | 0 |
| OUTPUT_TEST | 14 | 14 | 14 | 0 |
| CHANGEOVER_SWITCH | 12 | 12 | 12 | 0 (geometry now matches) |
| P01_FB | 0 | 0 | 0 | 0 |
| AI_COND | 204 | 214 | 194 | **-10** |
| SeimensLogic | 1116 | 1184 | 1184 | **+68** |

Remaining gaps: AI_COND **-10** means the single-junction/single-collinear-pair merge is
**too eager** on ~10 AI_COND nets (good keeps those arms separate at 3-arm junctions).
SeimensLogic **+68** is unchanged because its surpluses are multi-junction fan-out nets
(degree>3), which are deliberately not rebuilt. Refining the AI_COND rule needs the actual
good geometry of one of those 10 nets (not byte-comparable in TriStation here).

### 2.6 Sheet model

- A POU with a **FACESHEET** emits `FACESHEET` as sheet 1 (macros + its comment(s)),
  then the logic sheet(s). `P01_FB` has no facesheet: its single sheet is the logic
  sheet titled `P01_G006A`.
- The FACESHEET comment(s) appear **first** in the stream (FB04_DI: `21, 1`), and its
  second copy id = `original + PRIMARY_SPAN` (`1 -> 21`).
- Some sheets carry a trailing **empty** sheet (`sheetTitle=""`, empty `displayables`)
  — observed in FB04_DI (3 sheets: FACESHEET, logic, empty).
- `network` on blocks/wires: `901`/`115` etc are per-sheet unique non-zero net ids for
  the secondary copy; the primary copy uses `network="0"` on wires and the block keeps a
  set value. Our old code wrote `network="2"` (the index) and invented
  `firstLineNumber`/`lineCount` and `sequence=1,2` — all wrong; the reference uses
  `sequence="0"` and no line numbers.

### 2.7a `network` attribute (verified: project-wide net-id, not derivable)

`network` on blocks/wires/vars is a **shared connectivity net-id** produced by
TriStation's net-tracing/compile pass:

- FB04_DI primary: `FBlock#2 (R_TRIG)` and `FBlock#3 (RS)` **share** `network="901"`;
  secondary `FBlock#22` = `115`. Wires carry their own ids (wire#17=`2194`,
  wire#36/37=`3112`, wire#40=`3123`).
- Values **recur across POUs** (`2194` ×12, `3112` ×9), and the **same source pin can
  yield different nets** (OUTPUT_TEST src=`2` -> `3118` vs `3119`). So it is neither a
  source/owner-pin function nor a per-POU counter.
- Sentinel values `0` (809), `65535` (93), `32768` (3) also occur.

No XML field carries this id and the traversal is not reproducible generically ->
**unrecoverable**. The builder emits `network="0"` on wires (`FbdGenerator.cs` EmitWire)
and `network="{v.Sheet}"` on program variable placements (EmitVar); `cmpfbd` normalizes
the attribute away.

### 2.7 Element ClassType map

| XML element | Stream ClassType |
| --- | --- |
| `comment` | `CDGMCommentXml` (children: `CData Name="text"`) |
| `block` (FB instance) | `CFBDFBlockXml` (inputs/outputs: `CDGMInputXml`/`CDGMOutputXml`) |
| a **function** call (stateless) | `CFBDFFunctionXml` |
| `inVariable` | `CFBDInOutVarXml`? no — `inVariable` -> `CFBDOutVarXml` for outputs; `inOutVariable` -> `CFBDInOutVarXml`; program input vars -> `CFBDOutVarXml` |
| a wire (`connection`) | `CDGMWireXml` + `Collection Name="positions"` of `CDGMPositionXml` |

(Exact in/out var class mapping must be confirmed per-direction: `inVariable` with only
`connectionPointOut` = source; `outVariable`/`inOutVariable` with `connectionPointIn`.)

## 3. Native library elements **(verified)**

Standard blocks/functions (`ADD`, `AND`, `BOOL`, `ABS`, `GT`, `EQ`, `NE`, `GE`, `LE`,
`LN`, `DT`, ...) are stored as an **empty `.INL` stream (0 bytes) + `.INL_crc` (64 B)**.
They have no generated body. Our builder was creating non-empty `.INL` streams for
these — that is wrong; emit 0-byte `.INL` + its SHA-256 `_crc`.

`IST`/`ST`/`DOC` triples appear only for FB types that carry ST source (`AIN`, etc.).

## 4. Remaining work / open questions

- [x] `PRIMARY_SPAN` formula: `offset == max primary localId` (2.5). Exact primary ids
      include unrecoverable edit-history gaps -> accepted deviation.
- [x] `varVTType`/`refCount`/`fbRefCount`/`flags` for every variable role (see below).
- [x] Displayable emission order (2.5a): authoring z-order, not derivable -> accepted.
- [x] `network` numbering rule (see 2.7a): a **shared connectivity net-id** assigned by
      TriStation's project-wide net-tracing pass. Not derivable from the XML ->
      unrecoverable; our builder emits `0` on wires (normalized away by `cmpfbd`).
- [ ] Confirm `dogTag`/`type`/`subType`/`extOptFlags` for snapshots from the library.
- [x] Confirm the `CDGMPositionXml` waypoint list = the XML `<connection>` `<position>`
      list transformed by 2.2 (FB04_DI matched: 2 points -> 2, 4 -> 4). **Verified with
      a twist** (see 2.5b): waypoints are the connection path transformed by 2.2 and
      **reversed** for non-branch nets (FB04_DI, TEMPLATES, SeimensLogic 100% match).
      Branch/tap-merge nets are decomposed by TriStation's orthogonal router, which is
      NOT a cleanly-derivable rule -> accepted deviation.
- [ ] Confirm empty trailing sheet rule.
- [ ] Confirm sheettemplate C/D selection from XML `ts:SHEETS@Template`.

## 5. Verification plan

Build `SULFUR1__2.xml` with the new builder, then:
1. `pt2conv diff <good.pt2x> <ours.pt2x>` -> expect `changed=2` (only the two
   timestamp-bearing streams) or 0.
2. `dirdiff` -> expect no structural diffs.
3. Open in TriStation 5.8.0 (licensed) -> must load with no error. Output is
   **UNVERIFIED** until then.
