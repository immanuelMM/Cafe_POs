/** Small hex colour helpers for runtime theming. */
export const HEX_RE = /^#[0-9a-fA-F]{6}$/

const toRgb = (hex: string): [number, number, number] => [
  parseInt(hex.slice(1, 3), 16),
  parseInt(hex.slice(3, 5), 16),
  parseInt(hex.slice(5, 7), 16),
]
const toHex = (c: number[]) => `#${c.map((v) => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0')).join('')}`

/** Mix with black by `amount` (0..1): the darker hover shade. */
export const darken = (hex: string, amount = 0.2) => toHex(toRgb(hex).map((v) => v * (1 - amount)))

/** WCAG relative luminance (0 = black, 1 = white). */
export function luminance(hex: string) {
  const [r, g, b] = toRgb(hex).map((v) => {
    const c = v / 255
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

/** Readable text colour on top of `hex`: white on dark backgrounds, dark ink on light ones. */
export const onColor = (hex: string) => (luminance(hex) > 0.4 ? '#1f1410' : '#ffffff')

const INK = '#1f1410'
const contrast = (a: string, b: string) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (hi + 0.05) / (lo + 0.05)
}

/** Up to three brand colours plus gradient settings (the shape of the branding API, minus the non-colour fields). */
export interface BrandPalette {
  primary: string
  secondary?: string | null
  tertiary?: string | null
  gradient?: boolean
  angle?: number
}

/** Valid colour stops in order: primary, then secondary, then tertiary (a tertiary without a secondary is ignored). */
export function paletteStops(p: BrandPalette): string[] {
  const stops = [p.primary]
  if (p.secondary && HEX_RE.test(p.secondary)) {
    stops.push(p.secondary)
    if (p.tertiary && HEX_RE.test(p.tertiary)) stops.push(p.tertiary)
  }
  return stops
}

export const isGradient = (p: BrandPalette) => !!p.gradient && paletteStops(p).length > 1

/** CSS `background` for brand surfaces: a linear-gradient when enabled (and 2+ colours), else the primary colour. */
export function brandBackground(p: BrandPalette): string {
  if (!isGradient(p)) return p.primary
  return `linear-gradient(${Math.round(p.angle ?? 135)}deg, ${paletteStops(p).join(', ')})`
}

/** Readable text colour across the whole brand surface (for a gradient: best worst-case contrast over all stops). */
export function onBrand(p: BrandPalette): string {
  if (!isGradient(p)) return onColor(p.primary)
  const stops = paletteStops(p)
  const worst = (ink: string) => Math.min(...stops.map((s) => contrast(s, ink)))
  return worst('#ffffff') >= worst(INK) ? '#ffffff' : INK
}
