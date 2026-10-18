/** Canvas 2D drawing for the in-app logo maker. Everything is drawn from code (no images/fonts), so toBlob() is never tainted. */

import { paletteStops, type BrandPalette } from '../../lib/color'

export type IconId = 'none' | 'cup' | 'bean' | 'leaf' | 'croissant' | 'heart' | 'star'
export type FontId = 'sans' | 'serif' | 'rounded' | 'script'
export type ShapeId = 'circle' | 'rounded' | 'square' | 'none'
export type LayoutId = 'above' | 'left' | 'icon' | 'text'
export type BgStyle = 'solid' | 'gradient'

export interface LogoSettings {
  icon: IconId
  text: string
  font: FontId
  textColor: string
  shape: ShapeId
  bgColor: string
  /** 'gradient' fills the shape with the brand colours/angle (needs a palette with 2+ colours; else solid). */
  bgStyle: BgStyle
  ring: boolean
  ringColor: string
  layout: LayoutId
}

/** 24x24 viewBox path data, filled with the text colour (even-odd, so inner shapes become holes). */
export const ICONS: { id: IconId; label: string; path: string }[] = [
  { id: 'none', label: 'None', path: '' },
  {
    id: 'cup',
    label: 'Coffee cup',
    path: 'M3 8h14v5a5 5 0 0 1-5 5H8a5 5 0 0 1-5-5V8z M17 9h2a3 3 0 0 1 0 6h-2v-1.8h2a1.2 1.2 0 0 0 0-2.4h-2z M2 19.5h16v2H2z M7 2.5h1.6v3.5H7z M11 2.5h1.6v3.5H11z',
  },
  {
    id: 'bean',
    label: 'Coffee bean',
    path: 'M12 2c-4 0-7 4-7 10s3 10 7 10 7-4 7-10S16 2 12 2z M11.2 3.5c-1.6 3 1.6 5.2 0 8.2s1.6 5.2 0 8.3l1.6.4c1.6-3-1.6-5.2 0-8.2s-1.6-5.2 0-8.3z',
  },
  {
    id: 'leaf',
    label: 'Leaf',
    path: 'M20 4C10 4 4 9 4 15c0 2 .7 3.6 2 5 1-5 4-8 9-10-4 3-6.5 6-7.5 10.5C16 20 20 14 20 4z',
  },
  {
    id: 'croissant',
    label: 'Croissant',
    path: 'M2 16c0-5 4-9 10-9s10 4 10 9l-3.5 1.5c-.3-3.500-2.700-6-6.500-6s-6.200 2.500-6.500 6z M9 7.500L10 12l-1.500.5z M15 7.500l-1 4.500 1.500.5z',
  },
  {
    id: 'heart',
    label: 'Heart',
    path: 'M12 21s-8-5.300-8-11a4.500 4.500 0 0 1 8-2.800A4.500 4.500 0 0 1 20 10c0 5.700-8 11-8 11z',
  },
  { id: 'star', label: 'Star', path: 'M12 2l3 6.500 7 .8-5.200 4.800 1.400 7L12 17.500 5.800 21.100l1.400-7L2 9.300l7-.8z' },
]

export const FONTS: { id: FontId; label: string; stack: string; weight: string; style: string }[] = [
  { id: 'sans', label: 'Sans', stack: 'Helvetica, Arial, sans-serif', weight: '700', style: 'normal' },
  { id: 'serif', label: 'Serif', stack: 'Georgia, "Times New Roman", serif', weight: '700', style: 'normal' },
  { id: 'rounded', label: 'Rounded', stack: '"Trebuchet MS", "Arial Rounded MT Bold", Verdana, sans-serif', weight: '700', style: 'normal' },
  { id: 'script', label: 'Script', stack: '"Brush Script MT", "Segoe Script", "Comic Sans MS", cursive', weight: '400', style: 'italic' },
]

export const SHAPES: { id: ShapeId; label: string }[] = [
  { id: 'circle', label: 'Circle' },
  { id: 'rounded', label: 'Rounded square' },
  { id: 'square', label: 'Square' },
  { id: 'none', label: 'None (transparent)' },
]

export const LAYOUTS: { id: LayoutId; label: string }[] = [
  { id: 'above', label: 'Icon above text' },
  { id: 'left', label: 'Icon left of text' },
  { id: 'icon', label: 'Icon only' },
  { id: 'text', label: 'Text only' },
]

export const DEFAULT_LOGO: LogoSettings = {
  icon: 'cup',
  text: 'Brew',
  font: 'rounded',
  textColor: '#ffffff',
  shape: 'circle',
  bgColor: '#3b2417',
  bgStyle: 'solid',
  ring: false,
  ringColor: '#b4561f',
  layout: 'above',
}

const STORAGE_KEY = 'cafe-pos.logo-maker'

export function loadLogoSettings(fallbackBg: string): LogoSettings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) return { ...DEFAULT_LOGO, bgColor: fallbackBg, ...(JSON.parse(raw) as Partial<LogoSettings>) }
  } catch {
    /* storage unavailable or corrupt: use defaults */
  }
  return { ...DEFAULT_LOGO, bgColor: fallbackBg }
}

export function saveLogoSettings(s: LogoSettings) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(s))
  } catch {
    /* ignore */
  }
}

function shapePath(shape: ShapeId, s: number, inset: number) {
  const p = new Path2D()
  const x = inset
  const w = s - inset * 2
  if (shape === 'circle') p.arc(s / 2, s / 2, w / 2, 0, Math.PI * 2)
  else if (shape === 'rounded') p.roundRect(x, x, w, w, w * 0.22)
  else p.rect(x, x, w, w)
  return p
}

/** Canvas equivalent of CSS linear-gradient(<angle>deg, ...stops) over a square of side `s`; null with fewer than 2 colours. */
function brandGradient(ctx: CanvasRenderingContext2D, s: number, p: BrandPalette): CanvasGradient | null {
  const stops = paletteStops(p)
  if (stops.length < 2) return null
  const a = ((p.angle ?? 135) * Math.PI) / 180
  const dx = Math.sin(a)
  const dy = -Math.cos(a)
  const half = (s * (Math.abs(dx) + Math.abs(dy))) / 2
  const c = s / 2
  const g = ctx.createLinearGradient(c - dx * half, c - dy * half, c + dx * half, c + dy * half)
  stops.forEach((col, i) => g.addColorStop(i / (stops.length - 1), col))
  return g
}

/** Draws the logo into a square canvas context of `s` CSS pixels (the caller applies any devicePixelRatio scale). */
export function drawLogo(ctx: CanvasRenderingContext2D, s: number, o: LogoSettings, palette?: BrandPalette) {
  ctx.clearRect(0, 0, s, s)
  const ringW = o.ring && o.shape !== 'none' ? s * 0.045 : 0
  if (o.shape !== 'none') {
    ctx.fillStyle = (o.bgStyle === 'gradient' && palette && brandGradient(ctx, s, palette)) || o.bgColor
    ctx.fill(shapePath(o.shape, s, 0))
    if (ringW) {
      ctx.strokeStyle = o.ringColor
      ctx.lineWidth = ringW
      ctx.stroke(shapePath(o.shape, s, ringW / 2 + s * 0.015))
    }
  }

  // Content box: keep clear of the ring and, for circles, of the corners.
  const pad = o.shape === 'none' ? s * 0.04 : o.shape === 'circle' ? s * 0.2 : s * 0.15
  const box = s - pad * 2
  const text = o.text.trim()
  const hasIcon = o.icon !== 'none' && o.layout !== 'text'
  const hasText = !!text && o.layout !== 'icon'
  const icon = ICONS.find((i) => i.id === o.icon)
  const font = FONTS.find((f) => f.id === o.font) ?? FONTS[0]
  ctx.fillStyle = o.textColor

  const drawIcon = (x: number, y: number, size: number) => {
    if (!icon?.path) return
    ctx.save()
    ctx.translate(x, y)
    ctx.scale(size / 24, size / 24)
    ctx.fill(new Path2D(icon.path), 'evenodd')
    ctx.restore()
  }
  /** Draw text centred in the rectangle, shrinking the font until it fits. */
  const drawText = (x: number, y: number, w: number, h: number) => {
    let px = h
    ctx.font = `${font.style} ${font.weight} ${px}px ${font.stack}`
    const m = ctx.measureText(text).width
    if (m > w) {
      px *= w / m
      ctx.font = `${font.style} ${font.weight} ${px}px ${font.stack}`
    }
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'
    ctx.fillText(text, x + w / 2, y + h / 2)
  }

  if (hasIcon && !hasText) {
    const size = box * 0.9
    drawIcon(pad + (box - size) / 2, pad + (box - size) / 2, size)
  } else if (!hasIcon && hasText) {
    drawText(pad, pad + box * 0.25, box, box * 0.5)
  } else if (hasIcon && hasText) {
    if (o.layout === 'left') {
      const iconSize = box * 0.4
      drawIcon(pad, pad + (box - iconSize) / 2, iconSize)
      const tx = pad + iconSize + box * 0.04
      drawText(tx, pad + box * 0.3, pad + box - tx, box * 0.4)
    } else {
      const iconSize = box * 0.5
      drawIcon(pad + (box - iconSize) / 2, pad + box * 0.04, iconSize)
      drawText(pad, pad + box * 0.58, box, box * 0.34)
    }
  }
}

/** Render the logo to a PNG blob (e.g. 512x512). */
export function renderLogoBlob(o: LogoSettings, size = 512, palette?: BrandPalette): Promise<Blob> {
  const c = document.createElement('canvas')
  c.width = c.height = size
  const ctx = c.getContext('2d')
  if (!ctx) return Promise.reject(new Error('Canvas is not supported in this browser.'))
  drawLogo(ctx, size, o, palette)
  return new Promise((resolve, reject) => c.toBlob((b) => (b ? resolve(b) : reject(new Error('Could not create the image.'))), 'image/png'))
}
