import type { DesignKind, IconKind, ShapeKind } from './types'

export interface CanvasPreset {
  id: string
  kind: DesignKind
  label: string
  /** logical pixels the editor works in */
  width: number
  height: number
  /** physical print size (banners only) */
  print?: { w: number; h: number; unit: 'in' | 'mm' }
}

export const PRESETS: CanvasPreset[] = [
  { id: 'logo-512', kind: 'logo', label: 'Square 512 x 512', width: 512, height: 512 },
  { id: 'logo-1024', kind: 'logo', label: 'Square 1024 x 1024', width: 1024, height: 1024 },
  { id: 'banner-2x3', kind: 'banner', label: '2 x 3 ft portrait', width: 1800, height: 2700, print: { w: 24, h: 36, unit: 'in' } },
  { id: 'banner-3x4', kind: 'banner', label: '3 x 4 ft portrait', width: 2700, height: 3600, print: { w: 36, h: 48, unit: 'in' } },
  { id: 'banner-4x3', kind: 'banner', label: '4 x 3 ft landscape', width: 3600, height: 2700, print: { w: 48, h: 36, unit: 'in' } },
  { id: 'banner-a3p', kind: 'banner', label: 'A3 portrait', width: 1754, height: 2480, print: { w: 297, h: 420, unit: 'mm' } },
  { id: 'banner-a3l', kind: 'banner', label: 'A3 landscape', width: 2480, height: 1754, print: { w: 420, h: 297, unit: 'mm' } },
]

export const presetsFor = (kind: DesignKind) => PRESETS.filter((p) => p.kind === kind)
export const defaultPreset = (kind: DesignKind) => presetsFor(kind)[0]
export const findPreset = (id: string) => PRESETS.find((p) => p.id === id)

export interface FontOption {
  label: string
  stack: string
}
/** Curated system / web-safe stacks (nothing to download, so canvas text and exports always match). */
export const FONTS: FontOption[] = [
  { label: 'System sans', stack: "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif" },
  { label: 'Arial', stack: 'Arial, Helvetica, sans-serif' },
  { label: 'Verdana', stack: 'Verdana, Geneva, sans-serif' },
  { label: 'Trebuchet', stack: "'Trebuchet MS', Helvetica, sans-serif" },
  { label: 'Tahoma', stack: 'Tahoma, Geneva, sans-serif' },
  { label: 'Century Gothic', stack: "'Century Gothic', 'Apple Gothic', sans-serif" },
  { label: 'Georgia', stack: 'Georgia, serif' },
  { label: 'Times New Roman', stack: "'Times New Roman', Times, serif" },
  { label: 'Palatino', stack: "'Palatino Linotype', Palatino, 'Book Antiqua', serif" },
  { label: 'Garamond', stack: 'Garamond, Baskerville, serif' },
  { label: 'Courier New', stack: "'Courier New', Courier, monospace" },
  { label: 'Impact', stack: "Impact, 'Arial Narrow Bold', sans-serif" },
  { label: 'Brush Script', stack: "'Brush Script MT', 'Segoe Script', cursive" },
  { label: 'Comic Sans', stack: "'Comic Sans MS', 'Chalkboard SE', cursive" },
]
export const DEFAULT_FONT = FONTS[0].stack

export interface ShapeOption {
  shape: ShapeKind
  label: string
}
export const SHAPES: ShapeOption[] = [
  { shape: 'rect', label: 'Rectangle' },
  { shape: 'roundRect', label: 'Rounded rectangle' },
  { shape: 'circle', label: 'Circle' },
  { shape: 'ellipse', label: 'Ellipse' },
  { shape: 'triangle', label: 'Triangle' },
  { shape: 'star', label: 'Star' },
  { shape: 'polygon', label: 'Polygon' },
  { shape: 'line', label: 'Line' },
  { shape: 'arrow', label: 'Arrow' },
  { shape: 'heart', label: 'Heart' },
  { shape: 'bubble', label: 'Speech bubble' },
  { shape: 'ring', label: 'Ring' },
]

/**
 * Path data lives in a 100 x 100 box and only uses absolute M, L, Q, C and Z commands, so it can be stretched to any
 * element box without stretching the stroke. Icons are drawn with the even-odd rule: sub-paths inside another one are holes.
 */
export const PATHS: Record<'heart' | 'bubble', string> = {
  heart: 'M50 100 C8 66 0 40 0 27 C0 10 13 0 27 0 C38 0 46 6 50 15 C54 6 62 0 73 0 C87 0 100 10 100 27 C100 40 92 66 50 100 Z',
  bubble: 'M12 0 L88 0 Q100 0 100 12 L100 66 Q100 78 88 78 L42 78 L20 100 L26 78 L12 78 Q0 78 0 66 L0 12 Q0 0 12 0 Z',
}

const STEAM_BAND = 'M20 6 Q8 22 20 38 Q32 54 20 70 Q12 80 20 94 L28 94 Q20 80 28 70 Q40 54 28 38 Q16 22 28 6 Z'
const shiftX = (d: string, dx: number) => {
  let i = 0
  return d.replace(/-?\d+(\.\d+)?/g, (n) => (i++ % 2 === 0 ? String(Number(n) + dx) : n))
}

export const ICON_PATHS: Record<IconKind, string> = {
  cup: [
    'M10 34 L70 34 L66 72 Q64 84 52 84 L28 84 Q16 84 14 72 Z',
    'M70 42 L80 42 Q92 42 92 54 Q92 66 80 66 L68 66 L69 59 L78 59 Q83 59 83 54 Q83 49 78 49 L70 49 Z',
    'M4 90 L78 90 Q78 98 66 98 L16 98 Q4 98 4 90 Z',
    'M26 6 Q19 13 26 19 Q33 25 26 30 L31 30 Q38 25 31 19 Q24 13 31 6 Z',
    'M48 6 Q41 13 48 19 Q55 25 48 30 L53 30 Q60 25 53 19 Q46 13 53 6 Z',
  ].join(' '),
  bean: [
    'M50 2 C80 2 94 28 94 50 C94 76 76 98 50 98 C24 98 6 76 6 50 C6 28 20 2 50 2 Z',
    'M48 10 C36 30 62 50 46 90 L54 90 C70 50 44 30 56 10 Z',
  ].join(' '),
  leaf: ['M8 92 C8 40 40 8 92 8 C92 60 60 92 8 92 Z', 'M22 80 L66 34 L69 37 L25 83 Z'].join(' '),
  croissant: [
    'M6 70 C4 40 28 14 50 14 C72 14 96 40 94 70 C90 76 84 74 80 66 C74 70 66 70 62 62 C58 68 42 68 38 62 C34 70 26 70 20 66 C16 74 10 76 6 70 Z',
    'M33 21 L37 19 L42 57 L38 58 Z',
    'M67 21 L63 19 L58 57 L62 58 Z',
    'M18 40 L22 36 L28 56 L24 58 Z',
    'M82 40 L78 36 L72 56 L76 58 Z',
  ].join(' '),
  cake: [
    'M10 40 Q10 30 20 30 L80 30 Q90 30 90 40 L90 48 Q86 54 82 48 Q78 42 74 48 Q70 56 64 48 Q60 42 54 48 Q50 56 44 48 Q40 42 36 48 Q30 56 26 48 Q22 42 18 48 Q14 54 10 48 Z',
    'M12 58 L88 58 L88 86 Q88 90 84 90 L16 90 Q12 90 12 86 Z',
    'M47 14 L53 14 L53 27 L47 27 Z',
    'M50 2 Q57 8 50 13 Q43 8 50 2 Z',
    'M4 94 L96 94 L96 98 L4 98 Z',
  ].join(' '),
  steam: [shiftX(STEAM_BAND, -6), shiftX(STEAM_BAND, 22), shiftX(STEAM_BAND, 50)].join(' '),
}

export const ICONS: { icon: IconKind; label: string }[] = [
  { icon: 'cup', label: 'Coffee cup' },
  { icon: 'bean', label: 'Coffee bean' },
  { icon: 'leaf', label: 'Leaf' },
  { icon: 'croissant', label: 'Croissant' },
  { icon: 'cake', label: 'Cake' },
  { icon: 'steam', label: 'Steam swirls' },
]
