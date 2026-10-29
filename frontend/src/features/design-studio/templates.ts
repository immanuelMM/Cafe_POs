import { isGradient, onBrand, paletteStops, type BrandPalette } from '../../lib/color'
import { blankDoc, makeImage, makeMenu, makeShape, makeText } from './factory'
import type { CanvasPreset } from './presets'
import { DEFAULT_FONT, FONTS } from './presets'
import { fitText } from './textMeasure'
import type { DesignKind, Fill, ShapeElement, StudioDoc, StudioElement, TextElement } from './types'

export interface TemplateCtx {
  preset: CanvasPreset
  brandName: string
  tagline: string | null
  palette: BrandPalette
  hasLogo: boolean
  address: string | null
  phone: string | null
  /** Every active catalog item grouped by category (for the photo menu template). */
  menuSections: MenuSectionCtx[]
}

export interface MenuItemCtx {
  name: string
  description: string | null
  /** photo path relative to the API origin, or null (a placeholder tile is drawn) */
  path: string | null
  /** one entry per variation; label is empty when the item has a single variation */
  prices: { label: string; price: string }[]
}

export interface MenuSectionCtx {
  name: string
  items: MenuItemCtx[]
}

export interface TemplateDef {
  id: string
  kind: DesignKind
  name: string
  description: string
  build: (ctx: TemplateCtx) => StudioDoc
}

const CREAM = '#fbf4ea'
const SERIF = FONTS.find((f) => f.label === 'Georgia')!.stack

export const brandFill = (p: BrandPalette): Fill =>
  isGradient(p) ? { type: 'gradient', stops: paletteStops(p), angle: p.angle ?? 135 } : { type: 'solid', color: p.primary }

const initials = (name: string) =>
  name
    .split(/[\s&]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0].toUpperCase())
    .join('') || 'B'

/** Small builders so each template reads as a list of boxes and texts. */
function kit(doc: StudioDoc, ctx: TemplateCtx) {
  const pal = { brand: ctx.palette.primary, ink: '#1f1410' }
  const out: StudioElement[] = []
  const accent = ctx.palette.secondary ?? '#b4561f'
  const shape = (s: ShapeElement['shape'], x: number, y: number, w: number, h: number, patch: Partial<ShapeElement> = {}, name?: string) => {
    const el = makeShape(s, doc, pal)
    out.push({ ...el, name: name ?? el.name, x, y, width: w, height: h, ...patch })
    return el
  }
  const text = (str: string, x: number, y: number, w: number, size: number, patch: Partial<TextElement> = {}, name?: string) => {
    const el = makeText('body', doc, pal, undefined, str)
    out.push({ ...el, name: name ?? (str.length > 18 ? `${str.slice(0, 18)}...` : str), x, y, width: w, fontSize: size, height: size * 1.25, bold: true, ...patch })
  }
  const logo = (x: number, y: number, size: number) => {
    const el = makeImage({ type: 'api', path: '/api/v1/branding/logo' }, { w: 1, h: 1 }, doc, 'Logo')
    out.push({ ...el, x, y, width: size, height: size, fit: 'contain' })
  }
  const menu = (x: number, y: number, w: number, h: number, patch: Partial<ReturnType<typeof makeMenu>> = {}) => {
    out.push({ ...makeMenu(doc, pal, accent), x, y, width: w, height: h, ...patch })
  }
  const photo = (path: string, x: number, y: number, w: number, h: number, name: string, cornerRadius = 0) => {
    const el = makeImage({ type: 'api', path }, { w: 1, h: 1 }, doc, name)
    out.push({ ...el, x, y, width: w, height: h, fit: 'cover', cornerRadius })
  }
  return { out, shape, text, logo, menu, photo, accent }
}

const solid = (color: string): Fill => ({ type: 'solid', color })

function logoBadge(ctx: TemplateCtx): StudioDoc {
  const doc = blankDoc('logo', ctx.preset.id)
  const W = doc.width
  const ink = onBrand(ctx.palette)
  const k = kit(doc, ctx)
  k.shape('circle', W * 0.03, W * 0.03, W * 0.94, W * 0.94, { fill: brandFill(ctx.palette) }, 'Badge')
  k.shape('circle', W * 0.09, W * 0.09, W * 0.82, W * 0.82, { fill: null, stroke: ink, strokeWidth: Math.max(2, W * 0.012) }, 'Ring')
  k.text(initials(ctx.brandName), W * 0.1, W * 0.27, W * 0.8, W * 0.34, { fill: solid(ink), fontFamily: SERIF, align: 'center' }, 'Initials')
  k.shape('roundRect', W * 0.36, W * 0.66, W * 0.28, Math.max(3, W * 0.012), { fill: solid(k.accent), cornerRadius: 4 }, 'Divider')
  k.text(ctx.brandName.toUpperCase(), W * 0.16, W * 0.72, W * 0.68, W * 0.062, { fill: solid(ink), letterSpacing: W * 0.006, align: 'center' }, 'Brand name')
  return { ...doc, elements: k.out }
}

function logoEmblem(ctx: TemplateCtx): StudioDoc {
  const doc = blankDoc('logo', ctx.preset.id)
  const W = doc.width
  const ink = onBrand(ctx.palette)
  const k = kit(doc, ctx)
  k.shape('roundRect', W * 0.03, W * 0.03, W * 0.94, W * 0.94, { fill: brandFill(ctx.palette), cornerRadius: W * 0.2 }, 'Tile')
  k.shape('icon', W * 0.23, W * 0.12, W * 0.54, W * 0.54, { fill: solid(ink), icon: 'cup' }, 'Cup')
  k.text(ctx.brandName, W * 0.1, W * 0.7, W * 0.8, W * 0.1, { fill: solid(ink), align: 'center' }, 'Brand name')
  if (ctx.tagline) k.text(ctx.tagline, W * 0.1, W * 0.83, W * 0.8, W * 0.045, { fill: solid(ink), bold: false, align: 'center', opacity: 0.9 }, 'Tagline')
  return { ...doc, elements: k.out }
}

function logoWordmark(ctx: TemplateCtx): StudioDoc {
  const doc = blankDoc('logo', ctx.preset.id)
  const W = doc.width
  const k = kit(doc, ctx)
  const words = ctx.brandName.split(/\s+/)
  const half = Math.ceil(words.length / 2)
  const lines = words.length > 1 ? [words.slice(0, half).join(' '), words.slice(half).join(' ')] : [ctx.brandName]
  const size = W * (lines.length > 1 ? 0.2 : 0.16)
  const top = W * 0.5 - (lines.length * size * 1.1) / 2 - W * 0.06
  lines.forEach((l, i) => k.text(l, W * 0.06, top + i * size * 1.1, W * 0.88, size, { fill: solid(ctx.palette.primary), fontFamily: SERIF, align: 'center' }, `Line ${i + 1}`))
  const yLine = top + lines.length * size * 1.1 + W * 0.03
  k.shape('roundRect', W * 0.3, yLine, W * 0.4, Math.max(3, W * 0.015), { fill: solid(k.accent), cornerRadius: 4 }, 'Divider')
  k.text(ctx.tagline ?? 'Coffee & more', W * 0.1, yLine + W * 0.05, W * 0.8, W * 0.06, { fill: solid(k.accent), bold: false, align: 'center', letterSpacing: W * 0.004 }, 'Tagline')
  return { ...doc, elements: k.out }
}

const footerText = (ctx: TemplateCtx) => [ctx.address, ctx.phone].filter(Boolean).join('   |   ') || 'Visit us today'

/** Classic layout; `photos` shows each item's photo beside it in the menu block (auto-fit keeps it inside the block). */
function bannerClassic(ctx: TemplateCtx, photos = false): StudioDoc {
  const doc = { ...blankDoc('banner', ctx.preset.id), background: solid(CREAM) }
  const { width: W, height: H } = doc
  const landscape = W > H
  const ink = onBrand(ctx.palette)
  const headH = landscape ? H * 0.2 : H * 0.17
  const footH = H * 0.07
  const k = kit(doc, ctx)
  k.shape('rect', 0, 0, W, headH, { fill: brandFill(ctx.palette) }, 'Header')
  const logoSize = headH * 0.66
  if (ctx.hasLogo) k.logo(W * 0.05, (headH - logoSize) / 2, logoSize)
  const tx = ctx.hasLogo ? W * 0.05 + logoSize + W * 0.03 : W * 0.05
  k.text(ctx.brandName, tx, headH * (ctx.tagline ? 0.2 : 0.3), W - tx - W * 0.04, headH * 0.34, { fill: solid(ink), align: 'left' }, 'Brand name')
  if (ctx.tagline) k.text(ctx.tagline, tx, headH * 0.62, W - tx - W * 0.04, headH * 0.14, { fill: solid(ink), bold: false, align: 'left', opacity: 0.92 }, 'Tagline')
  k.menu(W * 0.05, headH + H * 0.03, W * 0.9, H - headH - footH - H * 0.06, { columns: landscape ? 3 : 2, fontSize: Math.round(W * (landscape ? 0.0155 : 0.021)), accentColor: ctx.palette.primary, textColor: '#1f1410', priceColor: '#1f1410', showPhotos: photos })
  k.shape('rect', 0, H - footH, W, footH, { fill: brandFill(ctx.palette) }, 'Footer')
  k.text(footerText(ctx), W * 0.04, H - footH + footH * 0.3, W * 0.92, footH * 0.36, { fill: solid(ink), bold: false, align: 'center' }, 'Address and phone')
  return { ...doc, elements: k.out }
}

function bannerBold(ctx: TemplateCtx): StudioDoc {
  const doc = { ...blankDoc('banner', ctx.preset.id), background: brandFill(ctx.palette) }
  const { width: W, height: H } = doc
  const landscape = W > H
  const ink = onBrand(ctx.palette)
  const k = kit(doc, ctx)
  const nameSize = W * (landscape ? 0.065 : 0.1)
  let y = H * 0.045
  if (ctx.hasLogo) {
    const s = H * (landscape ? 0.12 : 0.09)
    k.logo((W - s) / 2, y, s)
    y += s + H * 0.015
  }
  k.text(ctx.brandName.toUpperCase(), W * 0.04, y, W * 0.92, nameSize, { fill: solid(ink), align: 'center', letterSpacing: nameSize * 0.06, fontFamily: FONTS.find((f) => f.label === 'Impact')!.stack, bold: false }, 'Brand name')
  y += nameSize * 1.25
  if (ctx.tagline) {
    k.text(ctx.tagline, W * 0.06, y, W * 0.88, nameSize * 0.34, { fill: solid(ink), bold: false, align: 'center', italic: true, fontFamily: SERIF }, 'Tagline')
    y += nameSize * 0.5
  }
  k.shape('roundRect', W * 0.35, y, W * 0.3, Math.max(4, W * 0.006), { fill: solid(k.accent === ink ? CREAM : k.accent), cornerRadius: 6 }, 'Divider')
  y += H * 0.03
  const footH = H * 0.06
  k.menu(W * 0.06, y, W * 0.88, H - y - footH - H * 0.04, { columns: landscape ? 3 : 2, fontSize: Math.round(W * (landscape ? 0.0155 : 0.021)), textColor: ink, priceColor: ink, accentColor: ink })
  k.text(footerText(ctx), W * 0.04, H - footH, W * 0.92, footH * 0.4, { fill: solid(ink), bold: false, align: 'center' }, 'Address and phone')
  return { ...doc, elements: k.out }
}

function bannerMinimal(ctx: TemplateCtx): StudioDoc {
  const doc = { ...blankDoc('banner', ctx.preset.id), background: solid('#ffffff') }
  const { width: W, height: H } = doc
  const landscape = W > H
  const k = kit(doc, ctx)
  let y = H * 0.05
  if (ctx.hasLogo) {
    const s = H * (landscape ? 0.14 : 0.1)
    k.logo((W - s) / 2, y, s)
    y += s + H * 0.015
  }
  const size = W * (landscape ? 0.05 : 0.07)
  k.text(ctx.brandName, W * 0.05, y, W * 0.9, size, { fill: solid('#1f1410'), fontFamily: SERIF, align: 'center' }, 'Brand name')
  y += size * 1.3
  if (ctx.tagline) {
    k.text(ctx.tagline, W * 0.05, y, W * 0.9, size * 0.3, { fill: solid('#5e4d43'), bold: false, italic: true, fontFamily: SERIF, align: 'center' }, 'Tagline')
    y += size * 0.5
  }
  k.shape('rect', W * 0.42, y, W * 0.16, Math.max(3, W * 0.004), { fill: solid(ctx.palette.primary) }, 'Divider')
  y += H * 0.03
  const footH = H * 0.05
  k.menu(W * 0.07, y, W * 0.86, H - y - footH - H * 0.04, { columns: landscape ? 3 : 2, fontFamily: SERIF, fontSize: Math.round(W * (landscape ? 0.0155 : 0.021)), accentColor: ctx.palette.primary, textColor: '#1f1410', priceColor: ctx.palette.primary })
  k.text(footerText(ctx), W * 0.05, H - footH, W * 0.9, footH * 0.4, { fill: solid('#5e4d43'), bold: false, fontFamily: SERIF, align: 'center' }, 'Address and phone')
  return { ...doc, elements: k.out }
}

/**
 * Every item as a card (photo or placeholder tile, name, description, each variation with its price), grouped by
 * category; every part is a separate editable layer. Picks the fewest columns that make everything fit, then
 * shrinks the cards if even the widest grid overflows.
 */
function bannerPhotoMenu(ctx: TemplateCtx): StudioDoc {
  const doc = { ...blankDoc('banner', ctx.preset.id), background: solid(CREAM) }
  const { width: W, height: H } = doc
  const landscape = W > H
  const ink = onBrand(ctx.palette)
  const k = kit(doc, ctx)

  const headH = H * (landscape ? 0.16 : 0.12)
  const footH = H * 0.055
  k.shape('rect', 0, 0, W, headH, { fill: brandFill(ctx.palette) }, 'Header')
  const logoSize = headH * 0.66
  if (ctx.hasLogo) k.logo(W * 0.04, (headH - logoSize) / 2, logoSize)
  const tx = ctx.hasLogo ? W * 0.04 + logoSize + W * 0.025 : W * 0.04
  k.text(ctx.brandName, tx, headH * (ctx.tagline ? 0.18 : 0.3), W - tx - W * 0.04, headH * 0.36, { fill: solid(ink), align: 'left' }, 'Brand name')
  if (ctx.tagline) k.text(ctx.tagline, tx, headH * 0.62, W - tx - W * 0.04, headH * 0.15, { fill: solid(ink), bold: false, align: 'left', opacity: 0.92 }, 'Tagline')
  k.shape('rect', 0, H - footH, W, footH, { fill: brandFill(ctx.palette) }, 'Footer')
  k.text(footerText(ctx), W * 0.04, H - footH + footH * 0.3, W * 0.92, footH * 0.38, { fill: solid(ink), bold: false, align: 'center' }, 'Address and phone')

  const sections = ctx.menuSections.filter((s) => s.items.length > 0)
  const areaX = W * 0.04
  const areaW = W * 0.92
  const top = headH + H * 0.025
  const areaH = H - footH - H * 0.025 - top
  if (sections.length === 0) {
    k.text('Add items in Catalog, then apply this template again.', areaX, top + areaH * 0.45, areaW, W * 0.03, { fill: solid('#5e4d43'), bold: false, align: 'center' }, 'Hint')
    return { ...doc, elements: k.out }
  }

  // Card = square photo + name + optional description + one line per variation price. Text is measured (at a
  // reference card width; every size scales with the card) so a wrapped name or description gets the room it needs
  // instead of running into the next line. Every card gets the height of the tallest one so rows line up.
  const all = sections.flatMap((s) => s.items)
  const REF = 1000
  const LINE_HEIGHT = 1.15 // TextElement.lineHeight from makeText
  const PAD = 0.05
  const TEXT_GAP = 0.025
  type Piece = { text: string; size: number; h: number; bold: boolean; italic: boolean; tone: 'name' | 'desc' | 'price'; name: string }
  const measure = (it: MenuItemCtx): Piece[] => {
    const out: Piece[] = []
    const add = (text: string, size: number, maxLines: number, bold: boolean, italic: boolean, tone: Piece['tone'], name: string, minScale: number) => {
      const f = fitText(text, size * REF, (1 - PAD * 2) * REF, maxLines, { family: DEFAULT_FONT, bold, italic }, minScale)
      out.push({ text, size: f.size / REF, h: (f.lines * f.size * LINE_HEIGHT) / REF, bold, italic, tone, name })
    }
    // Names shrink a little to stay on one line, then wrap; descriptions may take two lines before shrinking.
    add(it.name, 0.105, 1, true, false, 'name', `${it.name} name`, 0.75)
    if (it.description) add(it.description, 0.062, 2, false, true, 'desc', `${it.name} description`, 0.8)
    for (const p of it.prices) add(p.label ? `${p.label}  ${p.price}` : p.price, 0.085, 1, true, false, 'price', `${it.name} ${p.label || 'price'}`, 0.7)
    return out
  }
  const pieces = new Map(all.map((it) => [it, measure(it)]))
  const contentH = (ps: Piece[]) => ps.reduce((h, p) => h + p.h, 0) + Math.max(0, ps.length - 1) * TEXT_GAP
  const CARD_RATIO = 1 + PAD * 0.6 + Math.max(...[...pieces.values()].map(contentH)) + PAD
  const GAP_RATIO = 0.08
  const HEAD_RATIO = 0.34
  const layoutHeight = (cols: number, cardW: number) => {
    const gap = cardW * GAP_RATIO
    return sections.reduce((h, s) => h + cardW * HEAD_RATIO + Math.ceil(s.items.length / cols) * (cardW * CARD_RATIO + gap), 0)
  }
  const widthFor = (cols: number) => areaW / (cols + (cols - 1) * GAP_RATIO)
  const maxCols = landscape ? 10 : 8
  let cols = landscape ? 3 : 2
  while (cols < maxCols && layoutHeight(cols, widthFor(cols)) > areaH) cols++
  let cardW = widthFor(cols)
  const total = layoutHeight(cols, cardW)
  if (total > areaH) cardW *= areaH / total
  const gap = cardW * GAP_RATIO
  const gridW = cols * cardW + (cols - 1) * gap
  const x0 = areaX + (areaW - gridW) / 2
  const tones = { name: '#1f1410', desc: '#5e4d43', price: ctx.palette.tertiary ?? k.accent }

  let y = top
  for (const s of sections) {
    const headSize = cardW * 0.16
    k.text(s.name.toUpperCase(), x0, y + cardW * 0.06, gridW, headSize, { fill: solid(ctx.palette.primary), align: 'left', letterSpacing: headSize * 0.08 }, s.name)
    k.shape('rect', x0, y + cardW * 0.06 + headSize * 1.3, gridW, Math.max(2, cardW * 0.012), { fill: solid(k.accent) }, `${s.name} rule`)
    y += cardW * HEAD_RATIO
    s.items.forEach((it, i) => {
      const cx = x0 + (i % cols) * (cardW + gap)
      const cy = y + Math.floor(i / cols) * (cardW * CARD_RATIO + gap)
      const pad = cardW * PAD
      const inner = cardW - pad * 2
      k.shape('roundRect', cx, cy, cardW, cardW * CARD_RATIO, { fill: solid('#ffffff'), stroke: null, cornerRadius: cardW * 0.06 }, `${it.name} card`)
      if (it.path) k.photo(it.path, cx + pad, cy + pad, inner, inner, `${it.name} photo`, cardW * 0.045)
      else {
        // Placeholder tile in the brand colours; swap it for a real photo later by adding one in Catalog.
        k.shape('roundRect', cx + pad, cy + pad, inner, inner, { fill: brandFill(ctx.palette), cornerRadius: cardW * 0.045 }, `${it.name} placeholder`)
        k.shape('icon', cx + pad + inner * 0.28, cy + pad + inner * 0.24, inner * 0.44, inner * 0.44, { fill: solid(ink), icon: 'cup', opacity: 0.85 }, `${it.name} icon`)
      }
      let ty = cy + cardW + pad * 0.6
      for (const p of pieces.get(it)!) {
        k.text(p.text, cx + pad, ty, inner, p.size * cardW, { fill: solid(tones[p.tone]), bold: p.bold, italic: p.italic, align: 'center', height: p.h * cardW }, p.name)
        ty += (p.h + TEXT_GAP) * cardW
      }
    })
    y += Math.ceil(s.items.length / cols) * (cardW * CARD_RATIO + gap)
  }
  return { ...doc, elements: k.out }
}

export const TEMPLATES: TemplateDef[] = [
  { id: 'logo-badge', kind: 'logo', name: 'Circle badge', description: 'Round badge with your initials', build: logoBadge },
  { id: 'logo-emblem', kind: 'logo', name: 'Cup emblem', description: 'Rounded tile with a coffee cup', build: logoEmblem },
  { id: 'logo-wordmark', kind: 'logo', name: 'Wordmark', description: 'Brand name in large type', build: logoWordmark },
  { id: 'banner-classic', kind: 'banner', name: 'Classic menu', description: 'Brand header, menu, contact footer', build: (ctx) => bannerClassic(ctx) },
  { id: 'banner-classic-photos', kind: 'banner', name: 'Classic menu with photos', description: 'Classic layout with each item’s photo', build: (ctx) => bannerClassic(ctx, true) },
  { id: 'banner-bold', kind: 'banner', name: 'Bold poster', description: 'Brand-colour poster with big name', build: bannerBold },
  { id: 'banner-minimal', kind: 'banner', name: 'Minimal', description: 'White page, serif type', build: bannerMinimal },
  { id: 'banner-photos', kind: 'banner', name: 'Photo menu', description: 'Every item as a card: photo, name, sizes and prices', build: bannerPhotoMenu },
]

export const templatesFor = (kind: DesignKind) => TEMPLATES.filter((t) => t.kind === kind)
