import Konva from 'konva'
import { formatMoney } from '../../lib/format'
import type { Item } from '../../lib/types'
import type { Section } from '../branding/menuSections'
import type { MenuElement } from './types'

export type MenuDraw =
  | {
      t: 'text'
      x: number
      y: number
      w: number
      text: string
      size: number
      bold: boolean
      color: string
      align: 'left' | 'right'
      opacity: number
    }
  | { t: 'rule'; x: number; y: number; w: number; h: number; color: string }
  | { t: 'photo'; x: number; y: number; size: number; path: string }

export interface MenuLayout {
  draws: MenuDraw[]
  /** true when the menu does not fit the block (items were dropped from the end) */
  overflow: boolean
  empty: boolean
  /** font size actually used (differs from the element's when "fit text" is on) */
  fontSize: number
}

const LINE = 1.2
let probe: Konva.Text | null = null
function measureRaw(text: string, size: number, bold: boolean, family: string, width?: number) {
  probe ??= new Konva.Text({ wrap: 'word' })
  probe.setAttrs({
    text,
    fontSize: size,
    fontFamily: family,
    fontStyle: bold ? 'bold' : 'normal',
    lineHeight: LINE,
    width: width ?? 'auto',
  })
  return { h: probe.height(), w: probe.getTextWidth() }
}

const price = (v: Item['variations'][number]) => formatMoney(v.effectivePrice ?? v.price)

interface Block {
  h: number
  draws: MenuDraw[]
}
interface Built {
  fs: number
  pad: number
  colW: number
  gap: number
  sections: { titleH: number; ruleH: number; headH: number; name: string; items: Block[] }[]
}

/** Measures every item for a font size: heights and relative draw lists, independent of where they end up. */
function build(el: MenuElement, sections: Section[], fs: number, cache: Map<string, { h: number; w: number }>): Built {
  const fam = el.fontFamily
  const pad = el.background ? fs * 0.8 : 0
  const cols = Math.max(1, Math.min(4, Math.round(el.columns)))
  const gap = fs * 1.6
  const colW = (el.width - pad * 2 - gap * (cols - 1)) / cols
  const measure = (text: string, size: number, bold: boolean, width?: number) => {
    const k = `${text}|${size}|${bold}|${width ?? ''}`
    let m = cache.get(k)
    if (!m) cache.set(k, (m = measureRaw(text, size, bold, fam, width)))
    return m
  }

  const itemBlock = (item: Item): Block => {
    const out: MenuDraw[] = []
    const photo = el.showPhotos && item.imageUrl ? fs * 3.6 : 0
    const tx = photo ? photo + fs * 0.7 : 0
    const tw = colW - tx
    let y = 0
    if (photo && item.imageUrl) out.push({ t: 'photo', x: 0, y: 0, size: photo, path: item.imageUrl })
    const row = (label: string, p: string, size: number, bold: boolean, indent: number) => {
      const pw = p ? measure(p, size, true).w : 0
      const nameW = Math.max(fs, tw - indent - (p ? pw + fs * 0.6 : 0))
      const { h } = measure(label, size, bold, nameW)
      out.push({
        t: 'text',
        x: tx + indent,
        y,
        w: nameW,
        text: label,
        size,
        bold,
        color: el.textColor,
        align: 'left',
        opacity: 1,
      })
      if (p)
        out.push({
          t: 'text',
          x: tx + tw - pw,
          y,
          w: pw,
          text: p,
          size,
          bold: true,
          color: el.priceColor,
          align: 'right',
          opacity: 1,
        })
      y += h
    }
    const vars = item.variations
    if (vars.length === 1) {
      row(item.name, price(vars[0]), fs, true, 0)
    } else {
      row(item.name, '', fs, true, 0)
      for (const v of vars) row(v.name, price(v), fs * 0.9, false, fs * 0.8)
    }
    if (el.showDescriptions && item.description) {
      const { h } = measure(item.description, fs * 0.72, false, tw)
      out.push({
        t: 'text',
        x: tx,
        y,
        w: tw,
        text: item.description,
        size: fs * 0.72,
        bold: false,
        color: el.textColor,
        align: 'left',
        opacity: 0.7,
      })
      y += h
    }
    return { draws: out, h: Math.max(y, photo) }
  }

  return {
    fs,
    pad,
    colW,
    gap,
    sections: sections.map((s) => {
      const titleH = measure(s.category.name, fs * 1.5, true, colW).h
      const ruleH = Math.max(2, fs * 0.12)
      return {
        titleH,
        ruleH,
        headH: titleH + fs * 0.15 + ruleH + fs * 0.5,
        name: s.category.name,
        items: s.items.map(itemBlock),
      }
    }),
  }
}

/** Packs the measured blocks into columns no taller than `limitH`; headings stay with their first item. */
function pack(el: MenuElement, b: Built, limitH: number, innerH: number) {
  const cols = Math.max(1, Math.min(4, Math.round(el.columns)))
  const { fs, pad, colW, gap } = b
  const draws: MenuDraw[] = []
  let overflow = false
  let col = 0
  let y = 0
  let tallest = 0
  outer: for (const s of b.sections) {
    for (let i = 0; i < s.items.length; i++) {
      const block = s.items[i]
      const unitH = (i === 0 ? s.headH : 0) + block.h
      if (y > 0 && y + unitH > limitH) {
        col++
        y = 0
      }
      if (col >= cols) {
        overflow = true
        break outer
      }
      if (y === 0 && unitH > innerH) overflow = true
      const ox = pad + col * (colW + gap)
      let oy = pad + y
      if (i === 0) {
        draws.push({
          t: 'text',
          x: ox,
          y: oy,
          w: colW,
          text: s.name,
          size: fs * 1.5,
          bold: true,
          color: el.accentColor,
          align: 'left',
          opacity: 1,
        })
        draws.push({ t: 'rule', x: ox, y: oy + s.titleH + fs * 0.15, w: colW, h: s.ruleH, color: el.accentColor })
        oy += s.headH
      }
      for (const d of block.draws) draws.push({ ...d, x: d.x + ox, y: d.y + oy })
      y += unitH + fs * 0.7
      tallest = Math.max(tallest, y)
    }
    y += fs * 0.9
  }
  return { draws, overflow, tallest }
}

/** Balanced column flow (like CSS columns): the shortest column height that still fits every item in the available columns. */
function balanced(el: MenuElement, sections: Section[], fs: number, cache: Map<string, { h: number; w: number }>) {
  const built = build(el, sections, fs, cache)
  const innerH = el.height - built.pad * 2
  const cols = Math.max(1, Math.min(4, Math.round(el.columns)))
  const total = pack(el, built, Infinity, innerH).tallest
  let h = Math.max(fs * 2, total / cols)
  for (;;) {
    const r = pack(el, built, Math.min(h, innerH), innerH)
    if (!r.overflow || h >= innerH) return r
    h += fs * 0.4
  }
}

export function layoutMenu(el: MenuElement, sections: Section[]): MenuLayout {
  const cache = new Map<string, { h: number; w: number }>()
  const empty = sections.length === 0
  if (empty) return { draws: [], overflow: false, empty, fontSize: el.fontSize }
  if (!el.autoFit) {
    const r = balanced(el, sections, el.fontSize, cache)
    return { draws: r.draws, overflow: r.overflow, empty, fontSize: el.fontSize }
  }
  // Largest font that fits the block (capped so a short menu does not turn into giant type).
  let lo = 6
  let hi = Math.max(8, el.width * 0.04)
  if (balanced(el, sections, hi, cache).overflow === false) lo = hi
  else {
    for (let i = 0; i < 12; i++) {
      const mid = (lo + hi) / 2
      if (balanced(el, sections, mid, cache).overflow) hi = mid
      else lo = mid
    }
  }
  const fs = Math.round(lo * 10) / 10
  const r = balanced(el, sections, fs, cache)
  return { draws: r.draws, overflow: r.overflow, empty, fontSize: fs }
}
