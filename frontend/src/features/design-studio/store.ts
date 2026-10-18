import { create } from 'zustand'
import { blankDoc, newId } from './factory'
import type { CanvasPreset } from './presets'
import type { Background, StudioDoc, StudioElement } from './types'

/** Element edits are shallow patches; the editor UI only sends fields that exist on the element's kind. */
export type Patch = Record<string, unknown>

export type AlignMode = 'left' | 'hcenter' | 'right' | 'top' | 'vmiddle' | 'bottom'
export type OrderMode = 'front' | 'forward' | 'backward' | 'back'

const HISTORY_LIMIT = 100
const COALESCE_MS = 900

interface State {
  doc: StudioDoc
  selected: string[]
  past: StudioDoc[]
  future: StudioDoc[]
  dirty: boolean
  clipboard: StudioElement[]
  pasteCount: number
  coalesce: { key: string; at: number } | null

  load: (doc: StudioDoc, dirty?: boolean) => void
  markSaved: () => void
  select: (ids: string[]) => void
  toggleSelect: (id: string) => void
  selectAll: () => void
  commit: (doc: StudioDoc, key?: string) => void
  add: (els: StudioElement | StudioElement[]) => void
  update: (ids: string[], patch: Patch | ((el: StudioElement) => Patch), key?: string) => void
  remove: (ids: string[]) => void
  order: (ids: string[], mode: OrderMode) => void
  duplicate: (ids: string[]) => void
  copy: (ids: string[]) => void
  paste: () => void
  undo: () => void
  redo: () => void
  setBackground: (bg: Background, key?: string) => void
  setMeasured: (id: string, height: number) => void
  resize: (preset: CanvasPreset, scaleContent: boolean) => void
  align: (ids: string[], mode: AlignMode) => void
  nudge: (ids: string[], dx: number, dy: number) => void
}

/** Axis-aligned bounds of an element, taking rotation (about its top-left origin) into account. */
export function boundsOf(el: StudioElement) {
  const r = (el.rotation * Math.PI) / 180
  const cos = Math.cos(r)
  const sin = Math.sin(r)
  const pts = [
    [0, 0],
    [el.width, 0],
    [el.width, el.height],
    [0, el.height],
  ].map(([px, py]) => [el.x + px * cos - py * sin, el.y + px * sin + py * cos])
  const xs = pts.map((p) => p[0])
  const ys = pts.map((p) => p[1])
  const x = Math.min(...xs)
  const y = Math.min(...ys)
  return { x, y, width: Math.max(...xs) - x, height: Math.max(...ys) - y }
}

const clone = <T,>(v: T): T => structuredClone(v)

function scaleElement(el: StudioElement, sx: number, sy: number): StudioElement {
  const k = Math.min(sx, sy)
  const anisotropic = (el.kind === 'shape' && (el.shape === 'rect' || el.shape === 'roundRect')) || el.kind === 'menu' || el.kind === 'image'
  const cx = (el.x + el.width / 2) * sx
  const cy = (el.y + el.height / 2) * sy
  const width = el.width * (anisotropic ? sx : k)
  const height = el.height * (anisotropic ? sy : k)
  const next = { ...el, x: cx - width / 2, y: cy - height / 2, width, height } as StudioElement
  if (next.kind === 'text') {
    next.fontSize = Math.max(1, next.fontSize * k)
    next.letterSpacing *= k
    next.strokeWidth *= k
  } else if (next.kind === 'menu') {
    next.fontSize = Math.max(1, next.fontSize * k)
  } else {
    next.strokeWidth *= k
    next.cornerRadius *= k
  }
  if ('shadow' in next && next.shadow) next.shadow = { ...next.shadow, blur: next.shadow.blur * k, offsetX: next.shadow.offsetX * k, offsetY: next.shadow.offsetY * k }
  return next
}

export const useStudio = create<State>((set, get) => ({
  doc: blankDoc('logo'),
  selected: [],
  past: [],
  future: [],
  dirty: false,
  clipboard: [],
  pasteCount: 0,
  coalesce: null,

  load: (doc, dirty = false) => set({ doc, selected: [], past: [], future: [], dirty, coalesce: null }),
  markSaved: () => set({ dirty: false }),
  select: (ids) => set({ selected: ids }),
  toggleSelect: (id) => set((s) => ({ selected: s.selected.includes(id) ? s.selected.filter((i) => i !== id) : [...s.selected, id] })),
  selectAll: () => set((s) => ({ selected: s.doc.elements.filter((e) => !e.locked && !e.hidden).map((e) => e.id) })),

  commit: (doc, key) => {
    const s = get()
    const now = Date.now()
    const merge = !!key && s.coalesce?.key === key && now - s.coalesce.at < COALESCE_MS
    const ids = new Set(doc.elements.map((e) => e.id))
    set({
      doc,
      selected: s.selected.filter((i) => ids.has(i)),
      past: merge ? s.past : [...s.past.slice(-(HISTORY_LIMIT - 1)), s.doc],
      future: [],
      dirty: true,
      coalesce: key ? { key, at: now } : null,
    })
  },

  add: (els) => {
    const list = Array.isArray(els) ? els : [els]
    const { doc, commit } = get()
    commit({ ...doc, elements: [...doc.elements, ...list] })
    set({ selected: list.map((e) => e.id) })
  },

  update: (ids, patch, key) => {
    const { doc, commit } = get()
    const set_ = new Set(ids)
    commit(
      {
        ...doc,
        elements: doc.elements.map((e) => (set_.has(e.id) ? ({ ...e, ...(typeof patch === 'function' ? patch(e) : patch) } as StudioElement) : e)),
      },
      key,
    )
  },

  remove: (ids) => {
    const { doc, commit } = get()
    if (!ids.length) return
    const set_ = new Set(ids)
    commit({ ...doc, elements: doc.elements.filter((e) => !set_.has(e.id)) })
  },

  order: (ids, mode) => {
    const { doc, commit } = get()
    const sel = new Set(ids)
    let els = [...doc.elements]
    if (mode === 'front') els = [...els.filter((e) => !sel.has(e.id)), ...els.filter((e) => sel.has(e.id))]
    else if (mode === 'back') els = [...els.filter((e) => sel.has(e.id)), ...els.filter((e) => !sel.has(e.id))]
    else if (mode === 'forward') {
      for (let i = els.length - 2; i >= 0; i--) if (sel.has(els[i].id) && !sel.has(els[i + 1].id)) [els[i], els[i + 1]] = [els[i + 1], els[i]]
    } else {
      for (let i = 1; i < els.length; i++) if (sel.has(els[i].id) && !sel.has(els[i - 1].id)) [els[i], els[i - 1]] = [els[i - 1], els[i]]
    }
    if (els.every((e, i) => e === doc.elements[i])) return
    commit({ ...doc, elements: els })
  },

  duplicate: (ids) => {
    const { doc, commit } = get()
    const sel = new Set(ids)
    const copies = doc.elements
      .filter((e) => sel.has(e.id))
      .map((e) => ({ ...clone(e), id: newId(), name: `${e.name} copy`, x: e.x + 24, y: e.y + 24, locked: false }) as StudioElement)
    if (!copies.length) return
    commit({ ...doc, elements: [...doc.elements, ...copies] })
    set({ selected: copies.map((c) => c.id) })
  },

  copy: (ids) => {
    const sel = new Set(ids)
    const els = get().doc.elements.filter((e) => sel.has(e.id))
    if (els.length) set({ clipboard: clone(els), pasteCount: 0 })
  },

  paste: () => {
    const { clipboard, doc, commit, pasteCount } = get()
    if (!clipboard.length) return
    const n = pasteCount + 1
    const copies = clipboard.map((e) => ({ ...clone(e), id: newId(), x: e.x + 24 * n, y: e.y + 24 * n, locked: false }) as StudioElement)
    commit({ ...doc, elements: [...doc.elements, ...copies] })
    set({ selected: copies.map((c) => c.id), pasteCount: n })
  },

  undo: () => {
    const s = get()
    const prev = s.past[s.past.length - 1]
    if (!prev) return
    const ids = new Set(prev.elements.map((e) => e.id))
    set({ doc: prev, past: s.past.slice(0, -1), future: [s.doc, ...s.future], selected: s.selected.filter((i) => ids.has(i)), dirty: true, coalesce: null })
  },

  redo: () => {
    const s = get()
    const next = s.future[0]
    if (!next) return
    const ids = new Set(next.elements.map((e) => e.id))
    set({ doc: next, future: s.future.slice(1), past: [...s.past, s.doc], selected: s.selected.filter((i) => ids.has(i)), dirty: true, coalesce: null })
  },

  setBackground: (bg, key) => get().commit({ ...get().doc, background: bg }, key),

  // Text height is derived from layout; keep the stored value in sync without touching history or the dirty flag.
  setMeasured: (id, height) =>
    set((s) => {
      const el = s.doc.elements.find((e) => e.id === id)
      if (!el || Math.abs(el.height - height) < 1) return s
      return { doc: { ...s.doc, elements: s.doc.elements.map((e) => (e.id === id ? { ...e, height } : e)) } }
    }),

  resize: (preset, scaleContent) => {
    const { doc, commit } = get()
    const sx = preset.width / doc.width
    const sy = preset.height / doc.height
    commit({
      ...doc,
      presetId: preset.id,
      width: preset.width,
      height: preset.height,
      elements: scaleContent ? doc.elements.map((e) => scaleElement(e, sx, sy)) : doc.elements,
    })
  },

  align: (ids, mode) => {
    const { doc, commit } = get()
    const targets = doc.elements.filter((e) => ids.includes(e.id) && !e.locked)
    if (!targets.length) return
    const boxes = targets.map(boundsOf)
    const area =
      targets.length === 1
        ? { x: 0, y: 0, width: doc.width, height: doc.height }
        : (() => {
            const x = Math.min(...boxes.map((b) => b.x))
            const y = Math.min(...boxes.map((b) => b.y))
            return { x, y, width: Math.max(...boxes.map((b) => b.x + b.width)) - x, height: Math.max(...boxes.map((b) => b.y + b.height)) - y }
          })()
    const delta = new Map<string, { dx: number; dy: number }>()
    targets.forEach((e, i) => {
      const b = boxes[i]
      let dx = 0
      let dy = 0
      if (mode === 'left') dx = area.x - b.x
      if (mode === 'hcenter') dx = area.x + area.width / 2 - (b.x + b.width / 2)
      if (mode === 'right') dx = area.x + area.width - (b.x + b.width)
      if (mode === 'top') dy = area.y - b.y
      if (mode === 'vmiddle') dy = area.y + area.height / 2 - (b.y + b.height / 2)
      if (mode === 'bottom') dy = area.y + area.height - (b.y + b.height)
      delta.set(e.id, { dx, dy })
    })
    commit({
      ...doc,
      elements: doc.elements.map((e) => {
        const d = delta.get(e.id)
        return d ? { ...e, x: Math.round(e.x + d.dx), y: Math.round(e.y + d.dy) } : e
      }),
    })
  },

  nudge: (ids, dx, dy) => {
    const sel = new Set(ids)
    get().update(
      ids,
      (e) => (sel.has(e.id) && !e.locked ? { x: e.x + dx, y: e.y + dy } : {}),
      'nudge',
    )
  },
}))
