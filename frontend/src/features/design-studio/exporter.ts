import type Konva from 'konva'
import type { StudioDoc } from './types'

const MB = 1024 * 1024
/** Server limits (PUT /branding/logo is 1 MB, PUT /branding/tarpaulin is 15 MB) minus a little headroom. */
export const LOGO_MAX_BYTES = MB - 2048
export const TARPAULIN_MAX_BYTES = 15 * MB - 64 * 1024
export const LOGO_EXPORT_SIZES = [512, 384, 256]

/** Largest pixel ratio that keeps the bitmap inside what browsers can allocate (about 16 megapixels, 8000 px a side). */
export function maxPrintRatio(doc: StudioDoc): number {
  return Math.min(2, Math.sqrt(16_000_000 / (doc.width * doc.height)), 8000 / Math.max(doc.width, doc.height))
}

interface RenderOpts {
  pixelRatio: number
  mime?: 'image/png' | 'image/jpeg'
  quality?: number
}

/** Renders the artboard (no selection handles, guides or zoom) to an image blob. */
export async function renderBlob(stage: Konva.Stage, doc: StudioDoc, opts: RenderOpts): Promise<Blob> {
  const ui = stage.find<Konva.Layer>('.studio-ui')
  const prevScale = stage.scale() ?? { x: 1, y: 1 }
  const prevSize = stage.size()
  ui.forEach((l) => l.visible(false))
  stage.scale({ x: 1, y: 1 })
  stage.size({ width: doc.width, height: doc.height })
  try {
    const blob = await stage.toBlob({ x: 0, y: 0, width: doc.width, height: doc.height, pixelRatio: opts.pixelRatio, mimeType: opts.mime ?? 'image/png', quality: opts.quality })
    if (!blob) throw new Error('The browser could not render the design.')
    return blob as Blob
  } catch (e) {
    if (e instanceof DOMException && e.name === 'SecurityError') {
      throw new Error('An image on the canvas blocks export (cross-origin without CORS). Remove it or re-add it from your computer.')
    }
    throw e
  } finally {
    stage.scale(prevScale)
    stage.size(prevSize)
    ui.forEach((l) => l.visible(true))
    stage.batchDraw()
  }
}

export interface Exported {
  blob: Blob
  mime: string
  width: number
  height: number
}

/** 512 x 512 PNG for PUT /branding/logo (re-exported smaller when it exceeds 1 MB). */
export async function exportLogo(stage: Konva.Stage, doc: StudioDoc): Promise<Exported> {
  for (const px of LOGO_EXPORT_SIZES) {
    const blob = await renderBlob(stage, doc, { pixelRatio: px / doc.width })
    if (blob.size <= LOGO_MAX_BYTES) return { blob, mime: 'image/png', width: px, height: Math.round((doc.height * px) / doc.width) }
  }
  throw new Error('The logo is still larger than 1 MB at the smallest size. Simplify the design (fewer photos or gradients) and try again.')
}

/** Print-resolution banner for PUT /branding/tarpaulin: PNG, falling back to JPEG 0.9 and lower resolutions to stay under 15 MB. */
export async function exportBanner(stage: Konva.Stage, doc: StudioDoc): Promise<Exported> {
  const max = maxPrintRatio(doc)
  for (const f of [1, 0.8, 0.65, 0.5]) {
    const pixelRatio = max * f
    const dims = { width: Math.round(doc.width * pixelRatio), height: Math.round(doc.height * pixelRatio) }
    const png = await renderBlob(stage, doc, { pixelRatio })
    if (png.size <= TARPAULIN_MAX_BYTES) return { blob: png, mime: 'image/png', ...dims }
    const jpg = await renderBlob(stage, doc, { pixelRatio, mime: 'image/jpeg', quality: 0.9 })
    if (jpg.size <= TARPAULIN_MAX_BYTES) return { blob: jpg, mime: 'image/jpeg', ...dims }
  }
  throw new Error('The banner is larger than 15 MB even at reduced resolution. Simplify the design and try again.')
}

/** Full-quality PNG for download. */
export async function exportDownload(stage: Konva.Stage, doc: StudioDoc): Promise<Exported> {
  const pixelRatio = maxPrintRatio(doc)
  const blob = await renderBlob(stage, doc, { pixelRatio })
  return { blob, mime: 'image/png', width: Math.round(doc.width * pixelRatio), height: Math.round(doc.height * pixelRatio) }
}

/** Moderate-resolution PNG used for the print sheet (the printer rarely needs more than ~150 dpi at banner size). */
export async function exportPrint(stage: Konva.Stage, doc: StudioDoc): Promise<Blob> {
  return renderBlob(stage, doc, { pixelRatio: Math.min(maxPrintRatio(doc), 3200 / Math.max(doc.width, doc.height)) })
}

export function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 10_000)
}
