# PT2X decompiler — `pt2conv frompt2`

`pt2conv frompt2 <project.pt2x> <out.xml>` decompiles a TriStation `.pt2/.pt2x`
container (CFB/OLE) into the TriStation PLCopen XML that `buildxml` consumes. It runs
in-process — no TriStation install or licence. Output is validated against an official
TriStation 5.8.0 export of the same project (`SAN MARTIN LOGIC W_LABELStam.xml`).

This note records the wire format rules that were reverse-engineered and the known
limits. Ground truth for FBD bodies is a structural XML compare (`fbdstruct`) that
ignores whitespace.

## Stages

1. **Container / interface / tags** — read every stream from the CFB; each
   `Current/Elements/<pou>/<hexid>.FBD` is a `CFBDFDocXml`; tags come from
   `Current/Tagnames/Tagnames.csv`. POU order = `Current/Project.xml` LibID sequence.
2. **FBD bodies** — blocks, pins, geometry and wires.
3. **ST bodies, POU/project macros** — `POU_EXT` and `PROJECT_EXT`.
4. **Controller configuration** — the `<ts:CONTROLLER>` block.

Stages 1–3 reproduce the reference exactly (structural equality on all 15 FBD pous;
0 section diffs; 6216/6216 connections). Stage 4 reproduces everything recoverable
from the container. Stage 5 makes the output format-identical (line endings and
pretty-printing), so the whole file now matches the reference line-for-line apart
from the documented limits plus the two timestamp attributes.

## FBD body rules (Stage 2)

- **Pin position** — `y = base + spacing*i`, where `i` is the 0-based index of the pin
  within its source `Collection`. `base = 4` for functions (`CFBDFFunctionXml`),
  `base = 6` for stateful FBs (`CFBDFBlockXml`); `spacing = doubleSpace ? 4 : 2`.
  `relPosition x = -1` (inputs) or `blk.W + 1` (outputs).
- **Attribute order** — variable elements are `<inVariable localId height width>`;
  block elements are `<block localId width height typeName ...>`.
- **Negated pins** — source `invertedPin="True"` ⇔ ref `negated="true"` on the
  `<variable>` (inputs and outputs).
- **Collapsed blocks** — `snapEventId="0"` ⇔ `Collapsed` ⇔ ref omits `instanceName`.
- **Sheets** — `Width = hZoneCount*20 + 2`, `Height = vZoneCount*25 + 2`.
- **Coordinate origin** — positions are relative to the top-left corner of the drawing
  area (the sheet's 30 unit margin), so the usable drawing height of template D is
  `2100 - 2 * 30 = 2040`. An element whose bottom edge is below that falls outside the
  frame; the TriStation editor drops those, and so does the `genfbd` builder.
- **Pin-name export** — TriStation renames `MUX` data inputs `INk -> IN(k+1)`
  (`K,IN1,IN2,IN3` -> `K,IN2,IN3,IN4`). Handled by `ExportInPinName`; resolution still
  uses the source pin name.
- **POU_EXT macros** — ST-bodied pous omit the 15 macros; FBD pous keep them.

## Formatting (Stage 5)

The reference is fully pretty-printed: one element per line, 2-space steps, using
**CRLF** structural line endings and **no trailing newline**. Two kinds of newline are
*not* CRLF in the reference and keep a raw LF:

- text inside `<![CDATA[ ]]>` bodies whose payload is user text (the contentHeader
  `<Comment>`, FBD `<content>` comments, and macro `<ts:Value>` payloads), and
- the contentHeader `<Comment>` element text.

`ToCrlf` splits on structural newlines only and leaves CDATA / `<Comment>` regions
verbatim, so the 655 lone LFs land exactly where the reference has them (the ST body
CDATA already carries its own CRLF). `AppendPosition`/`Ln` drive the FBD body layout:

| level | indent |
| --- | --- |
| `body` / `FBD` | 8 / 10 |
| `SHEETS` / `SHEET` / `NETWORKDIVIDER` / `Macros` / `Value` | 12 / 14 / 16 / 16 / 18 |
| body items (`comment`/`block`/`inVariable`/`inOutVariable`/`outVariable`) | 12 |
| item children (`FBDITEM_EXT`, `COMMENT_EXT`, `position`, `connectionPointIn/Out`, `expression`) | 14 |
| `FBDITEM_EXT` leaves, `connectionPointIn` rel/connection, pin `variable` | 16/18/20/22 |

Empty `{input,inOut,output}Variables` self-close (` />`); non-empty macro `Value`
wraps CDATA; `position` self-closes with `ts:SheetNumber`.

## Controller configuration (Stage 4)

`<ts:CONTROLLER>` is emitted after `</configuration>` inside `<configurations>` and is
derived from the project's configuration streams:

| Element | Source |
| --- | --- |
| `PPD_CONFIGURATION` | `Workspace.Xml@Platform`; `MainProcessorId` = main-chassis MP board `ModuleID`; `MajorVersion/MinorVersion` are embedded constants (`11/3`) |
| `OPERATING_PARAMETERS` | `OpParams.xml` (`DisableSTOP`, `DisableRemoteChanges`, `AllowDisablePoints`, `TimeSync`, `AllowExpiredCertificate`, `GenerateAlarmForExpiredCertificate`, `NumberOfDaysBeforeAlarmGenerated`) |
| `COMMUNICATION` | `TSCommunication.xml` (`NetNodeName`, `UDPPortNumber`, `BaudRate`, `UseNetConnection`, `UseSerConnection`); `NODENUMBER`/`IP_ADDRESS` from `Current/Platform/Platform.Xml` `NodeIdentifiersList` (`TRINODE008=173.10.2.8,8`); `SERIAL_PORT_NAME` = `DefSerialPort` |
| `HARDWARE_ALLOCATION` | `HwConfiguration.xml` |
| `MEMORY_ALLOCATION` | `GlobalMemory.xml` `SectionMap` `MaxAllocated`, by `Description` code (fixed order, 14 children) |
| `SOE_CONFIGURATION` | `SOEGlobals.xml` (16 `SOE_BLOCK` + `SOE_TRIP`) |
| `PEER_CONFIGURATION` | `OpParams.xml` `PeerMaxSend`/`PeerMaxReceive` |

### Hardware allocation

- Skip the memory chassis (`ChassisNum = -1`).
- `ChassisNum = 0` -> `MAIN_CHASSIS`; otherwise `EXP_CHASSIS`
  (`ChassisNumber`, `Id = ChassisID`).
- `Type` = the chassis `Name` suffix after `": "` (e.g. `Main Chassis: 8120X_MAIN`).
- `Description` is an embedded catalogue:
  `8120X_MAIN` -> `Tricon CX Main Chassis`,
  `HD_EXP` -> `High Density Expansion Chassis`,
  `LD_EXP` -> `Low Density Expansion Chassis`.
- Boards: skip `SlotID < 0` (MP/PS) and `ModuleID = 259` (`---- Empty`).
  `MODULE ID = ModuleID`; `SLOT_ADDRESS` is 1-based within the chassis, normalised to
  the chassis's minimum `SlotID` (HD_EXP is 0-based, LD_EXP 1-based).

### Memory map (`Description` -> tag)

`MBN`→`MEMORY_UNALIASED_BOOL`, `MIN`→`MEMORY_UNALIASED_DINT`,
`MRN`→`MEMORY_UNALIASED_REAL`, `MBR`→`MEMORY_READALIASED_BOOL`,
`MIR`→`MEMORY_READALIASED_DINT`, `MRR`→`MEMORY_READALIASED_REAL`,
`MBW`→`MEMORY_READWRITEALIASED_BOOL`, `MIW`→`MEMORY_READWRITEALIASED_DINT`,
`MRW`→`MEMORY_READWRITEALIASED_REAL`, `IBR`→`INPUT_BOOL`, `IIR`→`INPUT_DINT`,
`IRR`→`INPUT_REAL`, `QBW`→`OUTPUT_BOOL`, `QIW`→`OUTPUT_DINT`.

### SOE

`SOE_BLOCK_TYPE` `SoeBlockType` `0`→`UNASSIGNED`, `2`→`HISTORY`; `MANUAL`/`REDUNDANT`
booleans -> `0/1`; `USE_UTC` -> `true/false`. `SOE_TRIP` from the root attrs
(`TripTagName`, `TripState`, `TimeAfterTrip`, `TimeBeforeTrip`, `TripDeadband`).

## Known limits (not recoverable from the `.pt2x`)

Two element sets in the reference cannot be produced from the container alone. The
`frompt2` output is therefore **UNVERIFIED** until opened in a licensed TriStation.

1. **`HardwareModuleAttribute` (2268 nodes).** The per-module `SETUP_PARAMETERS` and
   `MEMORYALLOCATION_PARAMETERS` attributes (e.g. `Left.AccessList.Client1.IpAddress`,
   `Right.Serial.Port4.Parity`, `InputDiscretes`) come from TriStation's embedded
   platform module-descriptor database. These strings exist nowhere in the project
   container or the extract tree. The wrapper elements are emitted, empty
   (`<ts:SETUP_PARAMETERS />`, `<ts:MEMORYALLOCATION_PARAMETERS />`).
2. **`LIBRARY_DATA` (2 nodes, `TCXLIB` + `TX1LIB`).** The `<LIBRARY>` entries are
   emitted, but their `LIBRARY_DATA` (base64 of a ~0.95 MB / 2.1 MB CFB) is *not* a
   stream in the container — it is the standard library as installed with TriStation.
   (`STDSHEET`/`STDLIB` have no `LIBRARY_DATA` in the reference either.)

3. **One tag field.** Tag `DELTA` (Address `MIN.0033`) has
   `BinNumber="14" BinOffset="1"` in the reference; every one of the other 1020 tags is
   `0/0`. The value `14` occurs nowhere in the container or extract, so it is emitted as
   `0/0`. This is the single residual line difference outside the two sets above.

Per project decision, only recoverable configuration is emitted; the sets above are
left out and this limitation is accepted.

## Verification

- `sectdiff.ps1 -Our <xml>` — section/attribute diff vs reference → 0.
- `poudiff_fix.ps1 -Our <xml>` — connection/variable diff → missing 0, extra 0.
- `census.ps1 -OurPath <xml>` — element counts → only `HardwareModuleAttribute` and
  `LIBRARY_DATA` differ.
- `fbdstruct.ps1 -refPath <ref> -ourPath <xml>` — structural FBD body compare → all 15
  pous `STRUCT-EQUAL`.
- `ctrldiff.ps1 -refPath <ref> -ourPath <xml>` — controller compare → 15 diffs, all in
  the two non-recoverable sets.

After stripping the controller + library blocks, masking the two timestamps and
normalising line endings, the reference and `frompt2` outputs are equal on 251164/251164
lines except for the single `DELTA` `BinNumber`/`BinOffset` line.
