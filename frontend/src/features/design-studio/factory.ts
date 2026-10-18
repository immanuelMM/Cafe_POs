import { onBrand, onColor } from '../../lib/color'
import { uuid } from '../../lib/uuid'
import { DEFAULT_FONT, defaultPreset, findPreset } from './presets'
import {
  DOC_VERSION,
  type Background,
  type DesignKind,
  type IconKind,
  type ImageElement,
  type MenuElement,
  type ShapeElement,
  type ShapeKind,
  type Shadow,
  type StudioDoc,
  type StudioElement,
  type TextElement,
} from './types'

export const newId = () => uuid()

export const DEFAULT_SHADOW: Shadow = { color: '#000000', blur: 12, offsetX: 4, offsetY: 6, opacity: 0.35 }

/** Colours new elements start with, derived from the brand and the current background. */
export interface Palette {
  brand: string
  ink: string
}

/** Readable text colour on the given page background. */
export function inkFor(bg: Background): string {
  if (bg.type === 'solid') return onColor(bg.color)
  if (bg.type === 'gradient') return onBrand({ primary: bg.stops[0], secondary: bg.stops[1], tertiary: bg.stops[2], gradient: true })
  if (bg.type === 'image') return onColor(bg.color)
  return '#1f1410'
}

const base = (name: string, w: number, h: number, cx: number, cy: number) => ({
  id: newId(),
  name,
  x: Math.round(cx - w / 2),
  y: Math.round(cy - h / 2),
  width: Math.round(w),
  height: Math.round(h),
  rotation: 0,
  opacity: 1,
  locked: false,
  hidden: false,
})

const SHAPE_NAMES: Record<ShapeKind, string> = {
  rect: 'Rectangle',
  roundRect: 'Rounded rectangle',
  circle: 'Circle',
  ellipse: 'Ellipse',
  triangle: 'Triangle',
  star: 'Star',
  polygon: 'Polygon',
  line: 'Line',
  arrow: 'Arrow',
  heart: 'Heart',
  bubble: 'Speech bubble',
  ring: 'Ring',
  icon: 'Icon',
}
const ICON_NAMES: Record<IconKind, string> = {
  cup: 'Coffee cup',
  bean: 'Coffee bean',
  leaf: 'Leaf',
  croissant: 'Croissant',
  cake: 'Cake',
  steam: 'Steam swirls',
}

export function makeShape(shape: ShapeKind, doc: StudioDoc, pal: Palette, at?: { x: number; y: number }, icon?: IconKind): ShapeElement {
  const u = Math.min(doc.width, doc.height)
  const cx = at?.x ?? doc.width / 2
  const cy = at?.y ?? doc.height / 2
  const size = u * 0.28
  let w = size
  let h = size
  if (shape === 'rect' || shape === 'roundRect' || shape === 'ellipse' || shape === 'bubble') {
    w = size * 1.5
    h = size
  }
  if (shape === 'line' || shape === 'arrow') {
    w = size * 1.6
    h = Math.max(4, Math.round(u * 0.012))
  }
  if (shape === 'bubble') h = size * 1.1
  const stroked = shape === 'line' || shape === 'arrow'
  const sw = stroked ? Math.max(4, Math.round(u * 0.012)) : 0
  return {
    ...base(icon ? ICON_NAMES[icon] : SHAPE_NAMES[shape], w, stroked ? sw : h, cx, cy),
    kind: 'shape',
    shape,
    icon,
    fill: stroked ? null : { type: 'solid', color: pal.brand },
    stroke: stroked ? pal.brand : null,
    strokeWidth: sw,
    cornerRadius: Math.round(u * 0.04),
    points: 5,
    innerRatio: shape === 'ring' ? 0.6 : 0.45,
    sides: 6,
    flipX: false,
    flipY: false,
    shadow: null,
  }
}

export type TextPreset = 'heading' | 'subheading' | 'body'

export function makeText(preset: TextPreset, doc: StudioDoc, pal: Palette, at?: { x: number; y: number }, text?: string): TextElement {
  const u = Math.min(doc.width, doc.height)
  const cx = at?.x ?? doc.width / 2
  const cy = at?.y ?? doc.height / 2
  const spec = {
    heading: { size: u * 0.09, text: 'Your heading', name: 'Heading', bold: true },
    subheading: { size: u * 0.055, text: 'Subheading', name: 'Subheading', bold: true },
    body: { size: u * 0.035, text: 'Add your text here', name: 'Text', bold: false },
  }[preset]
  const fontSize = Math.round(spec.size)
  const w = Math.min(doc.width * 0.8, fontSize * 11)
  return {
    ...base(spec.name, w, fontSize * 1.2, cx, cy),
    kind: 'text',
    text: text ?? spec.text,
    fontFamily: DEFAULT_FONT,
    fontSize,
    bold: spec.bold,
    italic: false,
    underline: false,
    align: 'center',
    letterSpacing: 0,
    lineHeight: 1.15,
    fill: { type: 'solid', color: pal.ink },
    stroke: null,
    strokeWidth: 0,
    shadow: null,
    curve: 0,
  }
}

export function makeImage(src: ImageElement['src'], nat: { w: number; h: number }, doc: StudioDoc, name = 'Image', at?: { x: number; y: number }): ImageElement {
  const cx = at?.x ?? doc.width / 2
  const cy = at?.y ?? doc.height / 2
  const max = Math.min(doc.width, doc.height) * 0.5
  const k = Math.min(max / nat.w, max / nat.h)
  return {
    ...base(name, nat.w * k, nat.h * k, cx, cy),
    kind: 'image',
    src,
    fit: 'cover',
    cornerRadius: 0,
    stroke: null,
    strokeWidth: 0,
    flipX: false,
    flipY: false,
    shadow: null,
  }
}

export function makeMenu(doc: StudioDoc, pal: Palette, brand: string): MenuElement {
  const w = doc.width * 0.86
  const h = doc.height * 0.6
  return {
    ...base('Menu block', w, h, doc.width / 2, doc.height / 2),
    kind: 'menu',
    columns: doc.width > doc.height ? 3 : 2,
    fontFamily: DEFAULT_FONT,
    fontSize: Math.round(doc.width * 0.02),
    autoFit: true,
    textColor: pal.ink,
    accentColor: brand,
    priceColor: pal.ink,
    background: null,
    showPhotos: false,
    showDescriptions: true,
  }
}

export function blankDoc(kind: DesignKind, presetId?: string): StudioDoc {
  const preset = (presetId && findPreset(presetId)) || defaultPreset(kind)
  return {
    version: DOC_VERSION,
    kind,
    presetId: preset.id,
    width: preset.width,
    height: preset.height,
    background: kind === 'logo' ? { type: 'transparent' } : { type: 'solid', color: '#ffffff' },
    elements: [],
  }
}

const isNum = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v)
const isObj = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)

/** Validates a document from the API / local draft. Returns null when it cannot be used. */
export function parseDoc(raw: unknown, kind: DesignKind): StudioDoc | null {
  if (!isObj(raw) || raw.kind !== kind || !isNum(raw.width) || !isNum(raw.height) || !Array.isArray(raw.elements)) return null
  // Future migrations hook in here (switch on raw.version). Only version 1 exists today.
  if (raw.version !== DOC_VERSION) return null
  if (raw.width < 16 || raw.height < 16 || raw.width > 12000 || raw.height > 12000) return null
  const bg = isObj(raw.background) && typeof raw.background.type === 'string' ? (raw.background as unknown as Background) : { type: 'transparent' as const }
  const elements = (raw.elements as unknown[]).filter(
    (e): e is StudioElement =>
      isObj(e) &&
      typeof e.id === 'string' &&
      ['shape', 'text', 'image', 'menu'].includes(e.kind as string) &&
      isNum(e.x) &&
      isNum(e.y) &&
      isNum(e.width) &&
      isNum(e.height),
  )
  return {
    version: DOC_VERSION,
    kind,
    presetId: typeof raw.presetId === 'string' ? raw.presetId : 'custom',
    width: raw.width,
    height: raw.height,
    background: bg,
    elements,
  }
}
