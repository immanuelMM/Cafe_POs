import { useRef, type DragEvent, type ReactNode } from 'react'
import { ICON_PATHS, ICONS, PATHS, SHAPES } from './presets'
import { DRAG_MIME, type AddPayload } from './useAdd'
import type { DesignKind, ShapeKind } from './types'
import { IMAGE_ACCEPT } from './images'
import { Section } from './controls'

const star = (n: number, inner: number) =>
  Array.from({ length: n * 2 }, (_, i) => {
    const r = (i % 2 === 0 ? 1 : inner) * 44
    const a = -Math.PI / 2 + (i * Math.PI) / n
    return `${(50 + Math.cos(a) * r).toFixed(1)},${(50 + Math.sin(a) * r).toFixed(1)}`
  }).join(' ')
const poly = (n: number) =>
  Array.from({ length: n }, (_, i) => {
    const a = -Math.PI / 2 + (i * 2 * Math.PI) / n
    return `${(50 + Math.cos(a) * 44).toFixed(1)},${(50 + Math.sin(a) * 44).toFixed(1)}`
  }).join(' ')

function ShapeThumb({ shape }: { shape: ShapeKind }) {
  const fill = 'currentColor'
  const stroke = {
    stroke: 'currentColor',
    strokeWidth: 7,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    fill: 'none',
  }
  let body: ReactNode
  switch (shape) {
    case 'rect':
      body = <rect x="8" y="22" width="84" height="56" fill={fill} />
      break
    case 'roundRect':
      body = <rect x="8" y="22" width="84" height="56" rx="14" fill={fill} />
      break
    case 'circle':
      body = <circle cx="50" cy="50" r="42" fill={fill} />
      break
    case 'ellipse':
      body = <ellipse cx="50" cy="50" rx="44" ry="30" fill={fill} />
      break
    case 'triangle':
      body = <polygon points="50,10 92,88 8,88" fill={fill} />
      break
    case 'star':
      body = <polygon points={star(5, 0.45)} fill={fill} />
      break
    case 'polygon':
      body = <polygon points={poly(6)} fill={fill} />
      break
    case 'line':
      body = <path d="M10 50H90" {...stroke} />
      break
    case 'arrow':
      body = <path d="M10 50H90M70 30L90 50L70 70" {...stroke} />
      break
    case 'heart':
      body = <path d={PATHS.heart} fill={fill} transform="translate(5 5) scale(.9)" />
      break
    case 'bubble':
      body = <path d={PATHS.bubble} fill={fill} transform="translate(5 5) scale(.9)" />
      break
    case 'ring':
      body = <path d="M50 8a42 42 0 1 0 .01 0ZM50 30a20 20 0 1 0 .01 0Z" fill={fill} fillRule="evenodd" />
      break
    default:
      body = null
  }
  return (
    <svg viewBox="0 0 100 100" width="36" height="36" aria-hidden="true">
      {body}
    </svg>
  )
}

function Tile({
  label,
  payload,
  onAdd,
  children,
}: {
  label: string
  payload: AddPayload
  onAdd: (p: AddPayload) => void
  children: ReactNode
}) {
  const onDragStart = (e: DragEvent<HTMLButtonElement>) => {
    e.dataTransfer.setData(DRAG_MIME, JSON.stringify(payload))
    e.dataTransfer.effectAllowed = 'copy'
  }
  return (
    <button
      type="button"
      className="studio-tile"
      draggable
      onDragStart={onDragStart}
      onClick={() => onAdd(payload)}
      title={`${label} (click or drag onto the canvas)`}
    >
      {children}
      <span>{label}</span>
    </button>
  )
}

interface Props {
  kind: DesignKind
  hasLogo: boolean
  onAdd: (p: AddPayload) => void
  onFiles: (files: File[]) => void
  onPickItem: () => void
  error: string | null
}

/** Left panel: everything that can be added to the canvas. Tap/click to add at the center, or drag onto the canvas. */
export function AddPanel({ kind, hasLogo, onAdd, onFiles, onPickItem, error }: Props) {
  const input = useRef<HTMLInputElement>(null)
  return (
    <>
      {error && (
        <div className="alert alert-error" role="alert">
          {error}
        </div>
      )}
      <Section title="Text">
        <div className="studio-tiles">
          <Tile label="Heading" payload={{ t: 'text', preset: 'heading' }} onAdd={onAdd}>
            <b className="studio-t-h">Aa</b>
          </Tile>
          <Tile label="Subheading" payload={{ t: 'text', preset: 'subheading' }} onAdd={onAdd}>
            <b className="studio-t-s">Aa</b>
          </Tile>
          <Tile label="Body text" payload={{ t: 'text', preset: 'body' }} onAdd={onAdd}>
            <span className="studio-t-b">Aa</span>
          </Tile>
        </div>
      </Section>
      <Section title="Shapes">
        <div className="studio-tiles">
          {SHAPES.map((s) => (
            <Tile key={s.shape} label={s.label} payload={{ t: 'shape', shape: s.shape }} onAdd={onAdd}>
              <ShapeThumb shape={s.shape} />
            </Tile>
          ))}
        </div>
      </Section>
      <Section title="Cafe icons">
        <div className="studio-tiles">
          {ICONS.map((i) => (
            <Tile key={i.icon} label={i.label} payload={{ t: 'shape', shape: 'icon', icon: i.icon }} onAdd={onAdd}>
              <svg viewBox="0 0 100 100" width="36" height="36" aria-hidden="true">
                <path d={ICON_PATHS[i.icon]} fill="currentColor" fillRule="evenodd" />
              </svg>
            </Tile>
          ))}
        </div>
      </Section>
      <Section title="Images">
        <input
          ref={input}
          type="file"
          accept={IMAGE_ACCEPT}
          multiple
          className="sr-only"
          aria-label="Upload images"
          onChange={(e) => {
            onFiles(Array.from(e.target.files ?? []))
            e.target.value = ''
          }}
        />
        <div className="studio-actions">
          <button type="button" className="btn btn-sm" onClick={() => input.current?.click()}>
            Upload image
          </button>
          <button
            type="button"
            className="btn btn-sm"
            disabled={!hasLogo}
            title={hasLogo ? '' : 'Upload a logo first'}
            onClick={() => onAdd({ t: 'logo' })}
          >
            Insert my logo
          </button>
          <button type="button" className="btn btn-sm" onClick={onPickItem}>
            Item photo
          </button>
        </div>
        <small className="muted">
          Uploads are downscaled to 2000 px and saved inside the design. You can also drop image files onto the canvas.
        </small>
      </Section>
      {kind === 'banner' && (
        <Section title="Menu">
          <div className="studio-tiles">
            <Tile label="Menu block" payload={{ t: 'menu' }} onAdd={onAdd}>
              <svg viewBox="0 0 100 100" width="36" height="36" aria-hidden="true">
                <path
                  d="M12 14h76M12 30h50M12 46h76M12 62h50M12 78h76"
                  stroke="currentColor"
                  strokeWidth="7"
                  strokeLinecap="round"
                />
              </svg>
            </Tile>
          </div>
          <small className="muted">Your live catalog as one movable, resizable block.</small>
        </Section>
      )}
    </>
  )
}
