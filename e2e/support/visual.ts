import fs from 'node:fs'
import path from 'node:path'
import { PNG } from 'pngjs'
import pixelmatch from 'pixelmatch'

/** Local-only screenshot folder (git-ignored). Baseline PNGs live directly in it; compare runs add _current/ and _diff/. */
export const VISUAL_DIR = path.resolve(import.meta.dirname, '..', process.env.VISUAL_DIR ?? '.visual-baseline-m1')
export const VISUAL_MODE = (process.env.VISUAL_MODE ?? '') as '' | 'capture' | 'compare'

export interface DiffRow {
  name: string
  diffPixels: number
  totalPixels: number
  percent: number
  note?: string
}

const summaryPath = () => path.join(VISUAL_DIR, '_diff', 'summary.json')

export function resetCompareDirs() {
  fs.rmSync(path.join(VISUAL_DIR, '_current'), { recursive: true, force: true })
  fs.rmSync(path.join(VISUAL_DIR, '_diff'), { recursive: true, force: true })
  fs.mkdirSync(path.join(VISUAL_DIR, '_current'), { recursive: true })
  fs.mkdirSync(path.join(VISUAL_DIR, '_diff'), { recursive: true })
  fs.writeFileSync(summaryPath(), '[]')
}

/** Pixel-diff a freshly captured PNG buffer against the baseline file of the same name. */
export function diffAgainstBaseline(name: string, current: Buffer): DiffRow {
  fs.writeFileSync(path.join(VISUAL_DIR, '_current', name), current)
  const basePath = path.join(VISUAL_DIR, name)
  let row: DiffRow
  if (!fs.existsSync(basePath)) {
    row = { name, diffPixels: -1, totalPixels: 0, percent: 100, note: 'NO BASELINE' }
  } else {
    const a = PNG.sync.read(fs.readFileSync(basePath))
    const b = PNG.sync.read(current)
    const w = Math.max(a.width, b.width)
    const h = Math.max(a.height, b.height)
    const pad = (p: PNG) => {
      const out = new PNG({ width: w, height: h })
      PNG.bitblt(p, out, 0, 0, p.width, p.height, 0, 0)
      return out
    }
    const diff = new PNG({ width: w, height: h })
    const n = pixelmatch(pad(a).data, pad(b).data, diff.data, w, h, { threshold: 0.1, includeAA: true })
    const sizeChanged = a.width !== b.width || a.height !== b.height
    row = {
      name,
      diffPixels: n,
      totalPixels: w * h,
      percent: (n / (w * h)) * 100,
      note: sizeChanged ? `SIZE ${a.width}x${a.height} -> ${b.width}x${b.height}` : undefined,
    }
    if (n > 0) fs.writeFileSync(path.join(VISUAL_DIR, '_diff', name), PNG.sync.write(diff))
  }
  const rows = JSON.parse(fs.readFileSync(summaryPath(), 'utf8')) as DiffRow[]
  rows.push(row)
  fs.writeFileSync(summaryPath(), JSON.stringify(rows, null, 2))
  return row
}

export function formatSummary(): string {
  const rows = JSON.parse(fs.readFileSync(summaryPath(), 'utf8')) as DiffRow[]
  rows.sort((x, y) => y.percent - x.percent)
  const lines = rows.map((r) => `${r.percent.toFixed(4).padStart(9)}%  ${r.name}${r.note ? `  [${r.note}]` : ''}`)
  const differing = rows.filter((r) => r.percent > 0).length
  return `\nVisual diff vs ${VISUAL_DIR}\n${lines.join('\n')}\n${rows.length} screenshots, ${differing} differ\n`
}
