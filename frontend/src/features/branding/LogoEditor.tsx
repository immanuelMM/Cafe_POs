import { useCallback, useEffect, useRef, useState } from 'react'
import { Modal } from '../../components/Modal'
import { errorMessage } from '../../lib/api'

const VIEW = 320

interface Props {
  file: File
  busy: boolean
  error: unknown
  onUse: (blob: Blob) => void
  onClose: () => void
  /** Dialog title and noun used in labels/messages. Defaults suit the logo flow. */
  title?: string
  noun?: string
  /** Export size in px (square), MIME type and quality (JPEG/WebP only). Default: 512 PNG. */
  outSize?: number
  mime?: 'image/png' | 'image/jpeg'
  quality?: number
  maxBytes?: number
  /** Offer a transparent background (PNG only). When false the background is always white. */
  allowTransparent?: boolean
}

/** Simple square-crop editor: zoom slider, drag-to-position, optional transparent or white background. Defaults to a 512x512 PNG logo. */
export function LogoEditor({
  file,
  busy,
  error,
  onUse,
  onClose,
  title = 'Adjust logo',
  noun = 'logo',
  outSize = 512,
  mime = 'image/png',
  quality,
  maxBytes = 1024 * 1024,
  allowTransparent = true,
}: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const [img, setImg] = useState<HTMLImageElement | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [zoom, setZoom] = useState(1)
  // Image centre relative to the crop centre, as a fraction of the crop size.
  const [pos, setPos] = useState({ x: 0, y: 0 })
  const [whiteChoice, setWhite] = useState(false)
  const white = whiteChoice || !allowTransparent
  const [localError, setLocalError] = useState<string | null>(null)
  const drag = useRef<{ x: number; y: number; px: number; py: number } | null>(null)

  useEffect(() => {
    const url = URL.createObjectURL(file)
    const el = new Image()
    el.onload = () => setImg(el)
    el.onerror = () => setLoadError('This image could not be read. Try another file.')
    el.src = url
    return () => URL.revokeObjectURL(url)
  }, [file])

  /** Drawn image size (as a fraction of the crop size) and the allowed offset range. */
  const geometry = useCallback(
    (z: number) => {
      if (!img) return null
      const cover = 1 / Math.min(img.naturalWidth, img.naturalHeight)
      const dw = img.naturalWidth * cover * z
      const dh = img.naturalHeight * cover * z
      return { dw, dh, mx: (dw - 1) / 2, my: (dh - 1) / 2 }
    },
    [img],
  )
  const clamp = useCallback(
    (p: { x: number; y: number }, z: number) => {
      const g = geometry(z)
      if (!g) return p
      return { x: Math.max(-g.mx, Math.min(g.mx, p.x)), y: Math.max(-g.my, Math.min(g.my, p.y)) }
    },
    [geometry],
  )

  const render = useCallback(
    (c: HTMLCanvasElement, size: number) => {
      const g = geometry(zoom)
      const ctx = c.getContext('2d')
      if (!img || !g || !ctx) return
      c.width = c.height = size
      ctx.clearRect(0, 0, size, size)
      if (white) {
        ctx.fillStyle = '#ffffff'
        ctx.fillRect(0, 0, size, size)
      }
      const p = clamp(pos, zoom)
      ctx.drawImage(img, (0.5 + p.x - g.dw / 2) * size, (0.5 + p.y - g.dh / 2) * size, g.dw * size, g.dh * size)
    },
    [img, zoom, pos, white, geometry, clamp],
  )

  useEffect(() => {
    if (canvasRef.current) render(canvasRef.current, VIEW)
  }, [render])

  const move = (dx: number, dy: number) => setPos((p) => clamp({ x: p.x + dx, y: p.y + dy }, zoom))

  const use = () => {
    setLocalError(null)
    const c = document.createElement('canvas')
    render(c, outSize)
    c.toBlob(
      (b) => {
        if (!b) setLocalError('Could not create the image.')
        else if (b.size > maxBytes)
          setLocalError(
            `The edited ${noun} is larger than ${Math.round(maxBytes / 1024 / 1024)} MB. Try a smaller or simpler image.`,
          )
        else onUse(b)
      },
      mime,
      quality,
    )
  }

  const shownError = loadError ?? localError ?? (error ? errorMessage(error) : null)
  return (
    <Modal
      title={title}
      onClose={onClose}
      footer={
        <>
          <button type="button" className="btn btn-grow" onClick={onClose}>
            Cancel
          </button>
          <button type="button" className="btn btn-primary btn-grow" disabled={!img || busy} onClick={use}>
            {busy ? 'Uploading…' : `Use this ${noun}`}
          </button>
        </>
      }
    >
      {shownError && (
        <div className="alert alert-error" role="alert">
          {shownError}
        </div>
      )}
      {!img && !loadError && <p className="muted">Loading image…</p>}
      <div className="crop-wrap">
        <canvas
          ref={canvasRef}
          className={`crop-canvas${white ? '' : ' checker'}`}
          width={VIEW}
          height={VIEW}
          tabIndex={0}
          role="img"
          aria-label={`${noun[0].toUpperCase()}${noun.slice(1)} crop area. Drag, or use the arrow keys, to position the image.`}
          onPointerDown={(e) => {
            e.currentTarget.setPointerCapture(e.pointerId)
            drag.current = { x: e.clientX, y: e.clientY, px: pos.x, py: pos.y }
          }}
          onPointerMove={(e) => {
            const d = drag.current
            if (!d) return
            const w = e.currentTarget.getBoundingClientRect().width
            setPos(clamp({ x: d.px + (e.clientX - d.x) / w, y: d.py + (e.clientY - d.y) / w }, zoom))
          }}
          onPointerUp={() => (drag.current = null)}
          onPointerCancel={() => (drag.current = null)}
          onKeyDown={(e) => {
            const step = 0.03
            const m: Record<string, [number, number]> = {
              ArrowLeft: [-step, 0],
              ArrowRight: [step, 0],
              ArrowUp: [0, -step],
              ArrowDown: [0, step],
            }
            if (m[e.key]) {
              e.preventDefault()
              move(...m[e.key])
            }
          }}
        />
      </div>
      <label className="field">
        <span className="field-label">Zoom</span>
        <input
          type="range"
          min={1}
          max={4}
          step={0.01}
          value={zoom}
          onChange={(e) => {
            const z = Number(e.target.value)
            setZoom(z)
            setPos((p) => clamp(p, z))
          }}
        />
      </label>
      {allowTransparent && (
        <fieldset className="opt-group">
          <legend>Background</legend>
          <div className="chips" role="radiogroup" aria-label="Background">
            <button
              type="button"
              role="radio"
              aria-checked={!white}
              className={!white ? 'tab tab-on' : 'tab'}
              onClick={() => setWhite(false)}
            >
              Transparent
            </button>
            <button
              type="button"
              role="radio"
              aria-checked={white}
              className={white ? 'tab tab-on' : 'tab'}
              onClick={() => setWhite(true)}
            >
              White
            </button>
          </div>
        </fieldset>
      )}
    </Modal>
  )
}
