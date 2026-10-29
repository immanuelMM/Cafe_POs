import type { Fill, ShapeElement } from './types'
import { ICON_PATHS, PATHS } from './presets'

/** Minimal Konva context surface used for path tracing (Konva's Context mirrors CanvasRenderingContext2D). */
export interface PathCtx {
  beginPath(): void
  closePath(): void
  moveTo(x: number, y: number): void
  lineTo(x: number, y: number): void
  quadraticCurveTo(cx: number, cy: number, x: number, y: number): void
  bezierCurveTo(c1x: number, c1y: number, c2x: number, c2y: number, x: number, y: number): void
  ellipse(x: number, y: number, rx: number, ry: number, rot: number, start: number, end: number, ccw?: boolean): void
  arcTo(x1: number, y1: number, x2: number, y2: number, r: number): void
  rect(x: number, y: number, w: number, h: number): void
}

interface Cmd {
  c: string
  p: number[]
}
const parseCache = new Map<string, Cmd[]>()
function parse(d: string): Cmd[] {
  let cmds = parseCache.get(d)
  if (!cmds) {
    cmds = []
    for (const m of d.matchAll(/([MLQCZ])([^MLQCZ]*)/g)) {
      cmds.push({ c: m[1], p: (m[2].match(/-?\d+(\.\d+)?/g) ?? []).map(Number) })
    }
    parseCache.set(d, cmds)
  }
  return cmds
}

/** Draws a 100x100 path scaled to w x h. */
export function tracePath(ctx: PathCtx, d: string, w: number, h: number) {
  const sx = w / 100
  const sy = h / 100
  for (const { c, p } of parse(d)) {
    switch (c) {
      case 'M':
        ctx.moveTo(p[0] * sx, p[1] * sy)
        break
      case 'L':
        ctx.lineTo(p[0] * sx, p[1] * sy)
        break
      case 'Q':
        ctx.quadraticCurveTo(p[0] * sx, p[1] * sy, p[2] * sx, p[3] * sy)
        break
      case 'C':
        ctx.bezierCurveTo(p[0] * sx, p[1] * sy, p[2] * sx, p[3] * sy, p[4] * sx, p[5] * sy)
        break
      case 'Z':
        ctx.closePath()
        break
    }
  }
}

export function traceRoundRect(ctx: PathCtx, w: number, h: number, radius: number) {
  const r = Math.max(0, Math.min(radius, w / 2, h / 2))
  ctx.beginPath()
  if (r === 0) {
    ctx.rect(0, 0, w, h)
  } else {
    ctx.moveTo(r, 0)
    ctx.arcTo(w, 0, w, h, r)
    ctx.arcTo(w, h, 0, h, r)
    ctx.arcTo(0, h, 0, 0, r)
    ctx.arcTo(0, 0, w, 0, r)
  }
  ctx.closePath()
}

/** Shapes drawn with the even-odd rule (holes). */
export const usesEvenOdd = (s: ShapeElement) => s.shape === 'ring' || s.shape === 'icon'

/** The arrow head size for a given stroke width. */
export const arrowHead = (strokeWidth: number) => Math.max(12, strokeWidth * 3.2)

/** Builds the outline of a shape element into ctx, in element-local coordinates (box = 0,0..width,height). */
export function traceShape(ctx: PathCtx, s: ShapeElement) {
  const { width: w, height: h } = s
  ctx.beginPath()
  switch (s.shape) {
    case 'rect':
      ctx.rect(0, 0, w, h)
      break
    case 'roundRect':
      traceRoundRect(ctx, w, h, s.cornerRadius)
      return
    case 'circle':
    case 'ellipse':
      ctx.ellipse(w / 2, h / 2, w / 2, h / 2, 0, 0, Math.PI * 2)
      break
    case 'triangle':
      ctx.moveTo(w / 2, 0)
      ctx.lineTo(w, h)
      ctx.lineTo(0, h)
      break
    case 'star': {
      const n = Math.max(3, Math.round(s.points))
      for (let i = 0; i < n * 2; i++) {
        const r = i % 2 === 0 ? 1 : s.innerRatio
        const a = -Math.PI / 2 + (i * Math.PI) / n
        const px = w / 2 + Math.cos(a) * r * (w / 2)
        const py = h / 2 + Math.sin(a) * r * (h / 2)
        if (i === 0) ctx.moveTo(px, py)
        else ctx.lineTo(px, py)
      }
      break
    }
    case 'polygon': {
      const n = Math.max(3, Math.round(s.sides))
      for (let i = 0; i < n; i++) {
        const a = -Math.PI / 2 + (i * 2 * Math.PI) / n
        const px = w / 2 + Math.cos(a) * (w / 2)
        const py = h / 2 + Math.sin(a) * (h / 2)
        if (i === 0) ctx.moveTo(px, py)
        else ctx.lineTo(px, py)
      }
      break
    }
    case 'line':
      ctx.moveTo(0, h / 2)
      ctx.lineTo(w, h / 2)
      return
    case 'arrow': {
      const hs = arrowHead(s.strokeWidth)
      ctx.moveTo(0, h / 2)
      ctx.lineTo(w, h / 2)
      ctx.moveTo(Math.max(0, w - hs), h / 2 - hs / 2)
      ctx.lineTo(w, h / 2)
      ctx.lineTo(Math.max(0, w - hs), h / 2 + hs / 2)
      return
    }
    case 'heart':
      tracePath(ctx, PATHS.heart, w, h)
      return
    case 'bubble':
      tracePath(ctx, PATHS.bubble, w, h)
      return
    case 'ring':
      ctx.ellipse(w / 2, h / 2, w / 2, h / 2, 0, 0, Math.PI * 2)
      ctx.moveTo(w / 2 + (w / 2) * s.innerRatio, h / 2)
      ctx.ellipse(w / 2, h / 2, (w / 2) * s.innerRatio, (h / 2) * s.innerRatio, 0, 0, Math.PI * 2)
      return
    case 'icon':
      tracePath(ctx, ICON_PATHS[s.icon ?? 'cup'], w, h)
      return
  }
  ctx.closePath()
}

/** CSS-style gradient line (0deg = towards the top, 90deg = towards the right) across a w x h box. */
export function gradientLine(angle: number, w: number, h: number) {
  const a = (angle * Math.PI) / 180
  const dx = Math.sin(a)
  const dy = -Math.cos(a)
  const len = Math.abs(w * dx) + Math.abs(h * dy)
  const cx = w / 2
  const cy = h / 2
  return {
    start: { x: cx - (dx * len) / 2, y: cy - (dy * len) / 2 },
    end: { x: cx + (dx * len) / 2, y: cy + (dy * len) / 2 },
  }
}

/** Konva fill props for a solid colour or a gradient over a w x h box. */
export function fillProps(fill: Fill | null, w: number, h: number): Record<string, unknown> {
  if (!fill) return {}
  if (fill.type === 'solid') return { fill: fill.color }
  const { start, end } = gradientLine(fill.angle, w, h)
  const stops = fill.stops.length > 1 ? fill.stops : [fill.stops[0] ?? '#000000', fill.stops[0] ?? '#000000']
  return {
    fillPriority: 'linear-gradient',
    fillLinearGradientStartPoint: start,
    fillLinearGradientEndPoint: end,
    fillLinearGradientColorStops: stops.flatMap((c, i) => [i / (stops.length - 1), c]),
  }
}

/** CSS gradient string for previews in the panels. */
export const cssFill = (fill: Fill | null) =>
  !fill
    ? 'transparent'
    : fill.type === 'solid'
      ? fill.color
      : `linear-gradient(${fill.angle}deg, ${fill.stops.join(', ')})`

export function hexToRgba(hex: string, alpha: number) {
  const m = /^#([0-9a-f]{6})$/i.exec(hex)
  if (!m) return hex
  const n = parseInt(m[1], 16)
  return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${alpha})`
}

/**
 * Arc path for curved text: the chord is `width` and the arc bulges by curve% of half the width (positive = arch up).
 * Only as much arc as the text needs is emitted (centred), so the text stays centred in its box.
 */
export function arcPathData(width: number, curve: number, textLen: number) {
  const sag = (Math.abs(curve) / 100) * (width / 2)
  const r = ((width * width) / 4 + sag * sag) / (2 * Math.max(sag, 1))
  const phi = Math.min(textLen / r, Math.PI * 1.98)
  const large = phi > Math.PI ? 1 : 0
  const cx = width / 2
  const pt = (cy: number, a: number) => `${(cx + r * Math.cos(a)).toFixed(2)} ${(cy + r * Math.sin(a)).toFixed(2)}`
  if (curve > 0) {
    const cy = r
    return `M${pt(cy, -Math.PI / 2 - phi / 2)} A${r} ${r} 0 ${large} 1 ${pt(cy, -Math.PI / 2 + phi / 2)}`
  }
  const cy = sag - r
  return `M${pt(cy, Math.PI / 2 + phi / 2)} A${r} ${r} 0 ${large} 0 ${pt(cy, Math.PI / 2 - phi / 2)}`
}
