"use client";

import {
  useCallback,
  useMemo,
  useRef,
  useState,
  type PointerEvent as ReactPointerEvent,
  type ReactNode,
} from "react";

// ---------------------------------------------------------------------------
// The JSON model pt2conv `tojson` emits for a TriStation .pt2/.pt2x container.
// Coordinates are the native export units stored in the container: 1 unit =
// 1 px at 100% zoom, and a size-D sheet spans 3270 x 2070. The drawing frame,
// its zone ticks and the title block are *not* stored in the container (the
// desktop app synthesises them), so we rebuild them here from the same
// geometry the SheetTemplate<size> background templates use.
// ---------------------------------------------------------------------------

export interface InspectVar {
  name: string;
  dataType: string;
  init: string;
  desc: string;
  section: string;
  isInstance: boolean;
}

export interface InspectPin {
  name: string;
  wire: number;
}

export interface InspectElemBase {
  id: number;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface InspectBlockElem extends InspectElemBase {
  kind: "block";
  typeName: string;
  instance: string;
  isFunction: boolean;
  inputs: InspectPin[];
  outputs: string[];
}

export interface InspectVarElem extends InspectElemBase {
  kind: "var";
  name: string;
  varType: string;
  annotation: string;
  annotate: boolean;
  inWire: number;
  outWire: number;
}

export interface InspectCommentElem extends InspectElemBase {
  kind: "comment";
  text: string;
  border: string;
  align: string;
  textSize: number;
  behindLogic: boolean;
}

export interface InspectUnknownElem extends InspectElemBase {
  kind: "unknown";
}

export type InspectElem = InspectBlockElem | InspectVarElem | InspectCommentElem | InspectUnknownElem;

export interface InspectWire {
  id: number;
  srcId: number;
  srcPin: string;
  sinkId: number;
  sinkPin: string;
  positions: number[][];
}

export interface InspectSheet {
  number: number;
  title: string;
  elems: InspectElem[];
  wires: InspectWire[];
}

export interface InspectDrawing {
  number: string;
  title: string;
  size: string;
  sheetTemplate: string;
  rev: string;
  hZones: number;
  vZones: number;
}

export interface InspectPou {
  name: string;
  pouType: string;
  returnType: string | null;
  title: string;
  drawing: InspectDrawing;
  vars: InspectVar[];
  referencedGlobals: string[];
  st: string | null;
  sheets: InspectSheet[];
}

export interface InspectTag {
  name: string;
  type: string;
  desc: string;
  address: string;
}

export interface InspectProject {
  projectName: string;
  pouCount: number;
  tagCount: number;
  pous: InspectPou[];
  tags: InspectTag[];
}

// ---------------------------------------------------------------- geometry

interface SheetSize {
  w: number;
  h: number;
  titleW: number;
  titleH: number;
}

const SHEET_SIZES: Record<string, SheetSize> = {
  A: { w: 990, h: 732, titleW: 318, titleH: 96 },
  B: { w: 1576, h: 976, titleW: 296, titleH: 96 },
  C: { w: 2070, h: 1570, titleW: 423, titleH: 156 },
  D: { w: 3270, h: 2070, titleW: 1014, titleH: 280 },
};

const INK = "#1f2937";
const WIRE = "#374151";
const FRAME = "#4b5563";
const PIN_DOT = "#1f2937";

function sheetSize(drawing: InspectDrawing | undefined): SheetSize {
  const letter = (drawing?.size || "D").toUpperCase();
  return SHEET_SIZES[letter] ?? SHEET_SIZES.D;
}

// Points (textSize is in points) to user units.
const PT = 96 / 72;

// The block header occupies part of the top edge; pins sit below it on a fixed
// 20-unit pitch (verified against RT_01 / DINT_TO_DWORD in the reference FBD).
function blockPinY(elem: InspectBlockElem, index: number): number {
  return elem.y + (elem.isFunction ? 40 : 60) + index * 20;
}

// ---------------------------------------------------------------- drawing

function wrapComment(text: string, maxWidth: number, fontSize: number): string[] {
  const approxChar = fontSize * 0.56;
  const maxChars = Math.max(4, Math.floor(maxWidth / approxChar));
  const lines: string[] = [];
  for (const rawLine of text.split("\n")) {
    if (rawLine.length <= maxChars) {
      lines.push(rawLine);
      continue;
    }
    let current = "";
    for (const word of rawLine.split(" ")) {
      if (current.length === 0) {
        current = word;
      } else if ((current + " " + word).length <= maxChars) {
        current += " " + word;
      } else {
        lines.push(current);
        current = word;
      }
    }
    if (current.length > 0) lines.push(current);
  }
  return lines.length > 0 ? lines : [""];
}

function CommentText({ elem }: { elem: InspectCommentElem }) {
  const fontSize = Math.max(6, (elem.textSize || 10) * PT);
  const lines = wrapComment(elem.text, Math.max(20, elem.w - 8), fontSize);
  const anchor = elem.align === "Center" ? "middle" : "start";
  const tx = elem.align === "Center" ? elem.x + elem.w / 2 : elem.x + 4;
  const lineHeight = fontSize * 1.18;
  return (
    <text
      x={tx}
      y={elem.y + 3 + fontSize * 0.85}
      textAnchor={anchor}
      fontSize={fontSize}
      fill={INK}
      fontFamily="Arial, Helvetica, sans-serif"
    >
      {lines.map((line, i) => (
        <tspan key={i} x={tx} dy={i === 0 ? 0 : lineHeight}>
          {line}
        </tspan>
      ))}
    </text>
  );
}

function CommentBox({ elem, front }: { elem: InspectCommentElem; front: boolean }) {
  const double = elem.border === "Double";
  const single = elem.border === "Single" || double;
  return (
    <g>
      {single ? (
        <rect
          x={elem.x}
          y={elem.y}
          width={elem.w}
          height={elem.h}
          fill={front ? "#ffffff" : "none"}
          stroke={INK}
          strokeWidth={1}
        />
      ) : null}
      {double ? (
        <rect
          x={elem.x + 3}
          y={elem.y + 3}
          width={Math.max(0, elem.w - 6)}
          height={Math.max(0, elem.h - 6)}
          fill="none"
          stroke={INK}
          strokeWidth={1}
        />
      ) : null}
      <CommentText elem={elem} />
    </g>
  );
}

function BlockShape({ elem }: { elem: InspectBlockElem }) {
  const header = elem.isFunction ? 40 : 60;
  const label = elem.isFunction ? elem.typeName : elem.instance || elem.typeName;
  const sub = elem.isFunction ? "" : elem.typeName;
  const fontSize = 13;
  return (
    <g>
      <rect x={elem.x} y={elem.y} width={elem.w} height={elem.h} fill="#ffffff" stroke={INK} strokeWidth={1.5} />
      {!elem.isFunction ? (
        <line x1={elem.x} y1={elem.y + header - 4} x2={elem.x + elem.w} y2={elem.y + header - 4} stroke={INK} strokeWidth={0.75} />
      ) : null}
      <text
        x={elem.x + elem.w / 2}
        y={elem.y + (elem.isFunction ? 26 : 22)}
        textAnchor="middle"
        fontSize={fontSize}
        fontWeight={600}
        fill={INK}
        fontFamily="Arial, Helvetica, sans-serif"
      >
        {label}
      </text>
      {sub ? (
        <text
          x={elem.x + elem.w / 2}
          y={elem.y + 46}
          textAnchor="middle"
          fontSize={fontSize - 1}
          fill={INK}
          fontFamily="Arial, Helvetica, sans-serif"
        >
          {sub}
        </text>
      ) : null}
      {elem.inputs.map((pin, i) => {
        const py = blockPinY(elem, i);
        return (
          <g key={`i-${pin.name}-${i}`}>
            <text
              x={elem.x + 6}
              y={py + 4}
              textAnchor="start"
              fontSize={12}
              fill={INK}
              fontFamily="Arial, Helvetica, sans-serif"
            >
              {pin.name}
            </text>
            <line x1={elem.x} y1={py} x2={elem.x - 10} y2={py} stroke={INK} strokeWidth={1} />
            <circle cx={elem.x - 10} cy={py} r={2.2} fill={PIN_DOT} />
          </g>
        );
      })}
      {elem.outputs.map((name, i) => {
        const py = blockPinY(elem, i);
        return (
          <g key={`o-${name}-${i}`}>
            <text
              x={elem.x + elem.w - 6}
              y={py + 4}
              textAnchor="end"
              fontSize={12}
              fill={INK}
              fontFamily="Arial, Helvetica, sans-serif"
            >
              {name}
            </text>
            <line x1={elem.x + elem.w} y1={py} x2={elem.x + elem.w + 10} y2={py} stroke={INK} strokeWidth={1} />
            <circle cx={elem.x + elem.w + 10} cy={py} r={2.2} fill={PIN_DOT} />
          </g>
        );
      })}
    </g>
  );
}

function TerminalShape({ elem }: { elem: InspectVarElem }) {
  return (
    <g>
      <rect x={elem.x} y={elem.y} width={elem.w} height={elem.h} fill="#ffffff" stroke={INK} strokeWidth={1} />
      {elem.outWire > 0 ? (
        <>
          <line x1={elem.x + elem.w} y1={elem.y + 4} x2={elem.x + elem.w} y2={elem.y + elem.h - 4} stroke={INK} strokeWidth={0.75} />
          <line x1={elem.x + elem.w} y1={elem.y + 10} x2={elem.x + elem.w + 10} y2={elem.y + 10} stroke={INK} strokeWidth={1} />
          <circle cx={elem.x + elem.w + 10} cy={elem.y + 10} r={2.2} fill={PIN_DOT} />
        </>
      ) : null}
      {elem.inWire > 0 ? (
        <>
          <line x1={elem.x} y1={elem.y + 10} x2={elem.x - 10} y2={elem.y + 10} stroke={INK} strokeWidth={1} />
          <circle cx={elem.x - 10} cy={elem.y + 10} r={2.2} fill={PIN_DOT} />
        </>
      ) : null}
      <text
        x={elem.x + elem.w / 2}
        y={elem.y + elem.h / 2 + 4}
        textAnchor="middle"
        fontSize={12}
        fontWeight={600}
        fill={INK}
        fontFamily="Arial, Helvetica, sans-serif"
      >
        {elem.name}
      </text>
      {elem.annotate && elem.annotation ? (
        <text
          x={elem.x + elem.w / 2}
          y={elem.y + elem.h + 13}
          textAnchor="middle"
          fontSize={11}
          fill={INK}
          fontFamily="Arial, Helvetica, sans-serif"
        >
          {elem.annotation}
        </text>
      ) : null}
    </g>
  );
}

// ---------------------------------------------------------------- title block

interface TbLabel {
  x: number;
  y: number;
  w: number;
  h: number;
  align: string;
  size: number;
  text: string;
}

const TB_BOXES: Array<[number, number, number, number]> = [
  [0, 0, 395, 280],
  [0, 70, 1014, 70],
  [198, 70, 197, 210],
  [0, 105, 395, 70],
  [395, 140, 50, 55],
  [914, 140, 100, 105],
  [395, 195, 619, 50],
  [0, 210, 395, 35],
];

const TB_LABELS: TbLabel[] = [
  { x: 395, y: 2, w: 75, h: 20, align: "Left", size: 10, text: "PROJECT" },
  { x: 404, y: 22, w: 610, h: 45, align: "Left", size: 24, text: "%PROJECT_NAME" },
  { x: 45, y: 80, w: 105, h: 15, align: "Center", size: 10, text: "ACTIVITY" },
  { x: 245, y: 80, w: 110, h: 15, align: "Center", size: 10, text: "DATE/TIME" },
  { x: 395, y: 72, w: 125, h: 20, align: "Left", size: 10, text: "DRAWING TITLE" },
  { x: 404, y: 92, w: 610, h: 45, align: "Left", size: 24, text: "%DRAWING_TITLE" },
  { x: 0, y: 107, w: 198, h: 19, align: "Left", size: 10, text: "CREATED BY" },
  { x: 198, y: 120, w: 197, h: 20, align: "Center", size: 12, text: "%DATE_CREATED %TIME_CREATED" },
  { x: 0, y: 142, w: 198, h: 19, align: "Left", size: 10, text: "MODIFIED BY" },
  { x: 0, y: 155, w: 198, h: 20, align: "Left", size: 12, text: "%MODIFIED_BY" },
  { x: 198, y: 155, w: 197, h: 20, align: "Center", size: 12, text: "%DATE_MODIFIED %TIME_MODIFIED" },
  { x: 0, y: 177, w: 198, h: 20, align: "Left", size: 10, text: "PRINTED BY" },
  { x: 0, y: 190, w: 198, h: 20, align: "Left", size: 12, text: "%PRINTED_BY" },
  { x: 198, y: 190, w: 197, h: 20, align: "Center", size: 12, text: "%DATE_PRINTED %TIME_PRINTED" },
  { x: 0, y: 212, w: 198, h: 21, align: "Left", size: 10, text: "APPROVED BY" },
  { x: 0, y: 225, w: 198, h: 20, align: "Left", size: 12, text: "%APPROVED_NAME" },
  { x: 198, y: 225, w: 197, h: 20, align: "Center", size: 12, text: "%APPROVED_TIME" },
  { x: 0, y: 247, w: 198, h: 21, align: "Left", size: 10, text: "SHEET TEMPLATE" },
  { x: 0, y: 260, w: 198, h: 20, align: "Left", size: 12, text: "%SHEET_TEMPLATE" },
  { x: 394, y: 142, w: 45, h: 15, align: "Left", size: 10, text: "SIZE" },
  { x: 400, y: 153, w: 40, h: 40, align: "Center", size: 24, text: "%DRAWING_SIZE" },
  { x: 446, y: 142, w: 110, h: 15, align: "Left", size: 10, text: "DRAWING NO." },
  { x: 455, y: 160, w: 460, h: 35, align: "Left", size: 18, text: "%DRAWING_NUMBER" },
  { x: 915, y: 142, w: 40, h: 20, align: "Left", size: 10, text: "REV." },
  { x: 919, y: 160, w: 95, h: 35, align: "Center", size: 18, text: "%REVISION" },
  { x: 395, y: 197, w: 125, h: 20, align: "Left", size: 10, text: "SHEET TITLE" },
  { x: 401, y: 214, w: 510, h: 30, align: "Left", size: 18, text: "%SHEET_TITLE" },
  { x: 915, y: 197, w: 60, h: 15, align: "Left", size: 10, text: "SHEET" },
  { x: 919, y: 214, w: 95, h: 30, align: "Center", size: 14, text: "%SHEET_NUMBER of %SHEET_COUNT" },
  { x: 395, y: 247, w: 105, h: 20, align: "Left", size: 10, text: "ELEMENT" },
  { x: 397, y: 257, w: 615, h: 23, align: "Left", size: 14, text: "%ELEMENT_NAME" },
];

function expandTb(text: string, vars: Record<string, string>): string {
  let out = text;
  for (const [k, v] of Object.entries(vars)) out = out.split(k).join(v);
  return out.replace(/%[A-Z0-9_]+/g, "").replace(/\s{2,}/g, " ").trim();
}

function TitleBlock({
  drawing,
  projectName,
  pouName,
  pouTitle,
  sheet,
  sheetCount,
}: {
  drawing: InspectDrawing;
  projectName: string;
  pouName: string;
  pouTitle: string;
  sheet: InspectSheet;
  sheetCount: number;
}) {
  const size = sheetSize(drawing);
  const bx = size.w - size.titleW;
  const by = size.h - size.titleH;
  const sx = size.titleW / 1014;
  const sy = size.titleH / 280;
  const vars: Record<string, string> = {
    "%PROJECT_NAME": projectName,
    "%DRAWING_TITLE": drawing.title,
    "%DRAWING_NUMBER": drawing.number,
    "%DRAWING_SIZE": drawing.size,
    "%REVISION": drawing.rev,
    "%SHEET_TEMPLATE": drawing.sheetTemplate,
    "%SHEET_TITLE": sheet.title || pouTitle,
    "%SHEET_NUMBER": String(sheet.number),
    "%SHEET_COUNT": String(sheetCount),
    "%ELEMENT_NAME": pouName,
  };
  return (
    <g>
      <rect x={bx} y={by} width={size.titleW} height={size.titleH} fill="#ffffff" />
      {TB_BOXES.map(([x, y, w, h], i) => (
        <rect
          key={i}
          x={bx + x * sx}
          y={by + y * sy}
          width={w * sx}
          height={h * sy}
          fill="none"
          stroke={INK}
          strokeWidth={1}
        />
      ))}
      {TB_LABELS.map((l, i) => {
        const text = expandTb(l.text, vars);
        if (!text) return null;
        const fs = l.size * PT * sx;
        const cx = bx + (l.x + (l.align === "Center" ? l.w / 2 : 3)) * sx;
        const cy = by + (l.y + l.h / 2) * sy;
        return (
          <text
            key={`t-${i}`}
            x={cx}
            y={cy}
            textAnchor={l.align === "Center" ? "middle" : "start"}
            dominantBaseline="middle"
            fontSize={fs}
            fill={INK}
            fontFamily="Arial, Helvetica, sans-serif"
          >
            {text}
          </text>
        );
      })}
    </g>
  );
}

// ---------------------------------------------------------------- frame

function Frame({ drawing }: { drawing: InspectDrawing }) {
  const size = sheetSize(drawing);
  const m = Math.round(size.w * (30 / 3270));
  const hz = Math.max(1, drawing.hZones || 16);
  const vz = Math.max(1, drawing.vZones || 8);
  const innerW = size.w - 2 * m;
  const innerH = size.h - 2 * m;
  const colW = innerW / hz;
  const rowH = innerH / vz;
  const letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
  const ticks: ReactNode[] = [];
  for (let i = 1; i < hz; i++) {
    const x = m + i * colW;
    ticks.push(<line key={`tv${i}`} x1={x} y1={0} x2={x} y2={m} stroke={FRAME} strokeWidth={1} />);
    ticks.push(<line key={`bv${i}`} x1={x} y1={size.h - m} x2={x} y2={size.h} stroke={FRAME} strokeWidth={1} />);
  }
  for (let i = 1; i < vz; i++) {
    const y = m + i * rowH;
    ticks.push(<line key={`th${i}`} x1={0} y1={y} x2={m} y2={y} stroke={FRAME} strokeWidth={1} />);
    ticks.push(<line key={`bh${i}`} x1={size.w - m} y1={y} x2={size.w} y2={y} stroke={FRAME} strokeWidth={1} />);
  }
  const labels: ReactNode[] = [];
  for (let i = 0; i < hz; i++) {
    const x = m + (i + 0.5) * colW;
    labels.push(
      <text key={`cn${i}`} x={x} y={m / 2 + 4} textAnchor="middle" fontSize={m * 0.6} fill={FRAME} fontFamily="Arial">
        {i + 1}
      </text>,
    );
    labels.push(
      <text key={`cb${i}`} x={x} y={size.h - m / 2 + 4} textAnchor="middle" fontSize={m * 0.6} fill={FRAME} fontFamily="Arial">
        {i + 1}
      </text>,
    );
  }
  for (let i = 0; i < vz; i++) {
    const y = m + (i + 0.5) * rowH;
    const ch = letters[i] ?? "";
    labels.push(
      <text key={`rl${i}`} x={m / 2} y={y + 4} textAnchor="middle" fontSize={m * 0.6} fill={FRAME} fontFamily="Arial">
        {ch}
      </text>,
    );
    labels.push(
      <text key={`rr${i}`} x={size.w - m / 2} y={y + 4} textAnchor="middle" fontSize={m * 0.6} fill={FRAME} fontFamily="Arial">
        {ch}
      </text>,
    );
  }
  return (
    <g>
      <rect x={0.75} y={0.75} width={size.w - 1.5} height={size.h - 1.5} fill="#ffffff" stroke={FRAME} strokeWidth={1.5} />
      <rect x={m} y={m} width={innerW} height={innerH} fill="none" stroke={FRAME} strokeWidth={1} />
      {ticks}
      {labels}
    </g>
  );
}

// ---------------------------------------------------------------- component

const ZOOMS = [0.5, 1, 1.5];

export function FbdSheet({
  sheet,
  drawing,
  pouName,
  pouTitle,
  projectName,
  sheetCount,
}: {
  sheet: InspectSheet;
  drawing?: InspectDrawing;
  pouName: string;
  pouTitle: string;
  projectName: string;
  sheetCount: number;
}) {
  const size = sheetSize(drawing);
  const [zoom, setZoom] = useState(1);
  const scroller = useRef<HTMLDivElement>(null);
  const drag = useRef<{ x: number; y: number; left: number; top: number } | null>(null);

  const fit = useCallback(() => {
    const el = scroller.current;
    if (!el) return;
    const z = Math.min(1.5, Math.max(0.1, (el.clientWidth - 8) / size.w));
    setZoom(Number(z.toFixed(3)));
  }, [size.w]);

  const onPointerDown = useCallback((e: ReactPointerEvent) => {
    const el = scroller.current;
    if (!el) return;
    drag.current = { x: e.clientX, y: e.clientY, left: el.scrollLeft, top: el.scrollTop };
    el.setPointerCapture(e.pointerId);
  }, []);
  const onPointerMove = useCallback((e: ReactPointerEvent) => {
    const el = scroller.current;
    const d = drag.current;
    if (!el || !d) return;
    el.scrollLeft = d.left - (e.clientX - d.x);
    el.scrollTop = d.top - (e.clientY - d.y);
  }, []);
  const onPointerUp = useCallback((e: ReactPointerEvent) => {
    drag.current = null;
    scroller.current?.releasePointerCapture(e.pointerId);
  }, []);

  const { behind, front, logic } = useMemo(() => {
    const behind: InspectCommentElem[] = [];
    const front: InspectCommentElem[] = [];
    const logic: InspectElem[] = [];
    for (const e of sheet.elems) {
      if (e.kind === "comment") {
        (e.behindLogic ? behind : front).push(e);
      } else {
        logic.push(e);
      }
    }
    return { behind, front, logic };
  }, [sheet]);

  const drawingForFrame: InspectDrawing = drawing ?? {
    number: "",
    title: "",
    size: "D",
    sheetTemplate: "",
    rev: "",
    hZones: 16,
    vZones: 8,
  };

  return (
    <div className="border border-nx-line bg-nx-white">
      <div className="flex flex-wrap items-center gap-2 border-b border-nx-line bg-nx-green-50 px-2 py-1.5">
        <span className="text-[10px] font-semibold uppercase tracking-wide text-nx-hint">Zoom</span>
        <button type="button" onClick={fit} className="border border-nx-line px-2 py-0.5 text-[10px] font-semibold text-nx-ink hover:bg-nx-white">
          Fit
        </button>
        {ZOOMS.map((z) => (
          <button
            key={z}
            type="button"
            onClick={() => setZoom(z)}
            className={`border px-2 py-0.5 text-[10px] font-semibold ${
              Math.abs(zoom - z) < 0.001 ? "border-nx-ink bg-nx-neon text-nx-ink" : "border-nx-line text-nx-hint hover:text-nx-ink"
            }`}
          >
            {Math.round(z * 100)}%
          </button>
        ))}
        <span className="ml-auto text-[10px] text-nx-hint">
          {sheet.elems.length} elements · {sheet.wires.length} wires · drag to pan
        </span>
      </div>
      <div
        ref={scroller}
        className="max-h-[70vh] cursor-grab overflow-auto bg-nx-white active:cursor-grabbing"
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
      >
        <svg
          role="img"
          aria-label={`FBD sheet ${sheet.number}`}
          width={size.w * zoom}
          height={size.h * zoom}
          viewBox={`0 0 ${size.w} ${size.h}`}
          style={{ display: "block" }}
        >
          <Frame drawing={drawingForFrame} />
          <TitleBlock
            drawing={drawingForFrame}
            projectName={projectName}
            pouName={pouName}
            pouTitle={pouTitle}
            sheet={sheet}
            sheetCount={sheetCount}
          />

          {behind.map((c) => (
            <CommentBox key={`cb-${c.id}`} elem={c} front={false} />
          ))}

          {sheet.wires.map((w) => {
            const pts = w.positions ?? [];
            if (pts.length < 2) return null;
            const d = pts.map((p, i) => `${i === 0 ? "M" : "L"}${p[0]} ${p[1]}`).join(" ");
            const first = pts[0];
            const last = pts[pts.length - 1];
            return (
              <g key={`w-${w.id}`}>
                <path d={d} fill="none" stroke={WIRE} strokeWidth={1.5} strokeLinejoin="round" strokeLinecap="round" />
                <circle cx={first[0]} cy={first[1]} r={2.4} fill={PIN_DOT} />
                <circle cx={last[0]} cy={last[1]} r={2.4} fill={PIN_DOT} />
              </g>
            );
          })}

          {logic.map((e) => {
            if (e.kind === "block") return <BlockShape key={`b-${e.id}`} elem={e} />;
            if (e.kind === "var") return <TerminalShape key={`v-${e.id}`} elem={e} />;
            return null;
          })}

          {front.map((c) => (
            <CommentBox key={`cf-${c.id}`} elem={c} front />
          ))}
        </svg>
      </div>
    </div>
  );
}
