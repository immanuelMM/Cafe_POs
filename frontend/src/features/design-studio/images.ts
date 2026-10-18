import { useEffect, useState } from 'react'
import { assetUrl } from '../branding/brandingApi'
import type { ImageSrc } from './types'

/** Longest side (px) an uploaded image is downscaled to before it is embedded in the document. */
export const MAX_IMAGE_SIDE = 2000
const MAX_FILE_BYTES = 25 * 1024 * 1024
export const IMAGE_ACCEPT = 'image/png,image/jpeg,image/webp,image/gif'

const LOGO_PATH = '/api/v1/branding/logo'

/** Absolute URL for an image source. The branding logo always resolves to the current (cache-busted) logo URL. */
export function resolveSrc(src: ImageSrc, logoUrl: string | null): string | null {
  if (src.type === 'data') return src.url
  if (src.path.startsWith(LOGO_PATH)) return logoUrl
  return assetUrl(src.path)
}

const cache = new Map<string, Promise<HTMLImageElement>>()

function loadOnce(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image()
    // Cross-origin API images must be requested with CORS or the exported canvas is tainted.
    img.crossOrigin = 'anonymous'
    img.onload = () => resolve(img)
    img.onerror = () => reject(new Error(`Could not load image ${url}`))
    img.src = url
  })
}

/**
 * Loads an image for canvas use. API responses are cached for a year without `Vary: Origin`, so a copy cached earlier by a
 * plain <img> (no CORS, hence no Access-Control-Allow-Origin header) would fail a CORS request. A distinct query string
 * gives the canvas its own cache entry, which is always fetched with CORS.
 */
const corsKey = (url: string) => (/^(data|blob):/.test(url) ? url : `${url}${url.includes('?') ? '&' : '?'}cors=1`)

export function loadImage(url: string): Promise<HTMLImageElement> {
  let p = cache.get(url)
  if (!p) {
    p = loadOnce(corsKey(url))
    cache.set(url, p)
    p.catch(() => cache.delete(url))
  }
  return p
}

export interface ImageState {
  image: HTMLImageElement | null
  failed: boolean
}

export function useStudioImage(url: string | null): ImageState {
  const [state, setState] = useState<{ url: string | null; image: HTMLImageElement | null; failed: boolean }>({ url: null, image: null, failed: false })
  useEffect(() => {
    if (!url) return
    let live = true
    loadImage(url).then(
      (image) => live && setState({ url, image, failed: false }),
      () => live && setState({ url, image: null, failed: true }),
    )
    return () => {
      live = false
    }
  }, [url])
  if (!url || state.url !== url) return { image: null, failed: false }
  return { image: state.image, failed: state.failed }
}

/** Mirrored copy of an image (cheap, cached per image/flip) so flipping never needs a negative node scale. */
const flipCache = new WeakMap<HTMLImageElement, Map<string, HTMLCanvasElement>>()
export function flippedImage(img: HTMLImageElement, flipX: boolean, flipY: boolean): CanvasImageSource {
  if (!flipX && !flipY) return img
  const key = `${flipX ? 1 : 0}${flipY ? 1 : 0}`
  let per = flipCache.get(img)
  if (!per) flipCache.set(img, (per = new Map()))
  let c = per.get(key)
  if (!c) {
    c = document.createElement('canvas')
    c.width = img.naturalWidth
    c.height = img.naturalHeight
    const ctx = c.getContext('2d')!
    ctx.translate(flipX ? c.width : 0, flipY ? c.height : 0)
    ctx.scale(flipX ? -1 : 1, flipY ? -1 : 1)
    ctx.drawImage(img, 0, 0)
    per.set(key, c)
  }
  return c
}

export interface EmbeddedImage {
  src: ImageSrc
  width: number
  height: number
}

/** Reads a local file, downscales it to MAX_IMAGE_SIDE and returns it as an embeddable data URL. */
export async function fileToEmbedded(file: File): Promise<EmbeddedImage> {
  if (!/^image\/(png|jpe?g|webp|gif)$/.test(file.type)) throw new Error('Please choose a PNG, JPEG, WebP or GIF image.')
  if (file.size > MAX_FILE_BYTES) throw new Error('That image is larger than 25 MB. Please choose a smaller one.')
  const objectUrl = URL.createObjectURL(file)
  try {
    const img = await loadOnce(objectUrl)
    const k = Math.min(1, MAX_IMAGE_SIDE / Math.max(img.naturalWidth, img.naturalHeight))
    const width = Math.max(1, Math.round(img.naturalWidth * k))
    const height = Math.max(1, Math.round(img.naturalHeight * k))
    const canvas = document.createElement('canvas')
    canvas.width = width
    canvas.height = height
    const ctx = canvas.getContext('2d')!
    ctx.drawImage(img, 0, 0, width, height)
    // Keep transparency (PNG) only when the picture actually has some; photos become much smaller JPEGs.
    let alpha = false
    if (file.type !== 'image/jpeg') {
      const data = ctx.getImageData(0, 0, width, height).data
      for (let i = 3; i < data.length; i += 4) {
        if (data[i] < 255) {
          alpha = true
          break
        }
      }
    }
    const url = alpha ? canvas.toDataURL('image/png') : canvas.toDataURL('image/jpeg', 0.88)
    return { src: { type: 'data', url }, width, height }
  } finally {
    URL.revokeObjectURL(objectUrl)
  }
}
