import { useState, type ReactNode } from 'react'
import { HEX_RE, paletteStops } from '../../lib/color'
import { useStudioEnv } from './env'
import { cssFill } from './geometry'
import { DEFAULT_SHADOW } from './factory'
import type { Fill, Shadow } from './types'

const fmt = (n: number) => String(Math.round(n * 100) / 100)

/** Number input that lets the user type freely (invalid/partial text is only applied once it parses). */
export function NumField({
  label,
  value,
  onChange,
  min,
  max,
  step = 1,
  disabled,
}: {
  label: string
  value: number
  onChange: (v: number) => void
  min?: number
  max?: number
  step?: number
  disabled?: boolean
}) {
  const [draft, setDraft] = useState<string | null>(null)
  return (
    <label className="sf">
      <span className="sf-label">{label}</span>
      <input
        type="number"
        inputMode="decimal"
        value={draft ?? fmt(value)}
        min={min}
        max={max}
        step={step}
        disabled={disabled}
        onFocus={() => setDraft(fmt(value))}
        onBlur={() => setDraft(null)}
        onChange={(e) => {
          setDraft(e.target.value)
          const n = e.target.valueAsNumber
          if (Number.isFinite(n)) onChange(Math.min(max ?? Infinity, Math.max(min ?? -Infinity, n)))
        }}
      />
    </label>
  )
}

export function Seg<T extends string | number>({
  label,
  value,
  options,
  onChange,
}: {
  label: string
  value: T
  options: { value: T; label: ReactNode; title?: string }[]
  onChange: (v: T) => void
}) {
  return (
    <div className="seg" role="group" aria-label={label}>
      {options.map((o) => (
        <button
          key={String(o.value)}
          type="button"
          className={`seg-btn${o.value === value ? ' seg-on' : ''}`}
          aria-pressed={o.value === value}
          title={o.title}
          onClick={() => onChange(o.value)}
        >
          {o.label}
        </button>
      ))}
    </div>
  )
}

export function Toggle({
  label,
  checked,
  onChange,
}: {
  label: string
  checked: boolean
  onChange: (v: boolean) => void
}) {
  return (
    <label className="check sf-check">
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} /> {label}
    </label>
  )
}

/** Quick picks: the brand colours plus white and the default ink. */
export function BrandSwatches({ onPick }: { onPick: (c: string) => void }) {
  const { palette } = useStudioEnv()
  const colors = [...new Set([...paletteStops(palette), '#ffffff', '#1f1410'].map((c) => c.toLowerCase()))]
  return (
    <div className="studio-swatches" role="group" aria-label="Brand colours">
      {colors.map((c) => (
        <button
          key={c}
          type="button"
          className="swatch studio-swatch"
          style={{ background: c }}
          aria-label={`Use ${c}`}
          title={c}
          onClick={() => onPick(c)}
        />
      ))}
    </div>
  )
}

export function ColorField({
  label,
  value,
  onChange,
  swatches = true,
}: {
  label: string
  value: string
  onChange: (c: string) => void
  swatches?: boolean
}) {
  const [draft, setDraft] = useState<string | null>(null)
  const valid = HEX_RE.test(value)
  return (
    <div className="sf-color">
      <span className="sf-label">{label}</span>
      <div className="sf-color-row">
        <input
          type="color"
          className="color-input"
          aria-label={`${label} picker`}
          value={valid ? value : '#000000'}
          onChange={(e) => onChange(e.target.value)}
        />
        <input
          className="hex-input"
          aria-label={`${label} hex`}
          maxLength={7}
          spellCheck={false}
          value={draft ?? value}
          onFocus={() => setDraft(value)}
          onBlur={() => setDraft(null)}
          onChange={(e) => {
            setDraft(e.target.value)
            if (HEX_RE.test(e.target.value)) onChange(e.target.value.toLowerCase())
          }}
        />
      </div>
      {swatches && <BrandSwatches onPick={onChange} />}
    </div>
  )
}

const DEFAULT_STOPS = ['#3b2417', '#b4561f', '#f2c48d']
const ANGLES = [90, 135, 180, 45]

/** Stops + angle editor shared by fills and the page background. */
export function GradientEditor({
  stops,
  angle,
  onChange,
}: {
  stops: string[]
  angle: number
  onChange: (stops: string[], angle: number) => void
}) {
  const { palette } = useStudioEnv()
  const brand = paletteStops(palette)
  return (
    <div className="sf-stack">
      {stops.map((c, i) => (
        <div key={i} className="sf-stop">
          <ColorField
            label={`Color ${i + 1}`}
            value={c}
            swatches={i === 0}
            onChange={(v) =>
              onChange(
                stops.map((s, j) => (j === i ? v : s)),
                angle,
              )
            }
          />
          {stops.length > 2 && (
            <button
              type="button"
              className="btn btn-ghost btn-icon"
              aria-label={`Remove color ${i + 1}`}
              onClick={() =>
                onChange(
                  stops.filter((_, j) => j !== i),
                  angle,
                )
              }
            >
              ×
            </button>
          )}
        </div>
      ))}
      <div className="row-actions">
        {stops.length < 3 && (
          <button
            type="button"
            className="btn btn-sm"
            onClick={() => onChange([...stops, DEFAULT_STOPS[stops.length] ?? '#ffffff'], angle)}
          >
            + Color
          </button>
        )}
        {brand.length > 1 && (
          <button type="button" className="btn btn-sm" onClick={() => onChange(brand, palette.angle ?? 135)}>
            Brand gradient
          </button>
        )}
      </div>
      <div className="angle-row" role="group" aria-label="Gradient angle">
        <input
          type="range"
          min={0}
          max={360}
          aria-label="Gradient angle in degrees"
          value={angle}
          onChange={(e) => onChange(stops, Number(e.target.value))}
        />
        <output>{angle}°</output>
      </div>
      <div className="row-actions">
        {ANGLES.map((a) => (
          <button
            key={a}
            type="button"
            className="btn btn-sm angle-btn"
            aria-pressed={angle === a}
            onClick={() => onChange(stops, a)}
          >
            {a}°
          </button>
        ))}
      </div>
    </div>
  )
}

export function FillEditor({
  label,
  fill,
  onChange,
  allowNone = false,
}: {
  label: string
  fill: Fill | null
  onChange: (f: Fill | null) => void
  allowNone?: boolean
}) {
  const { palette } = useStudioEnv()
  const mode = fill?.type ?? 'none'
  const options = [
    ...(allowNone ? [{ value: 'none', label: 'None' }] : []),
    { value: 'solid', label: 'Solid' },
    { value: 'gradient', label: 'Gradient' },
  ]
  const lastSolid = fill?.type === 'solid' ? fill.color : fill?.type === 'gradient' ? fill.stops[0] : palette.primary
  const brand = paletteStops(palette)
  return (
    <div className="sf-stack">
      <span className="sf-label">{label}</span>
      <Seg
        label={`${label} type`}
        value={mode}
        options={options}
        onChange={(m) => {
          if (m === 'none') onChange(null)
          else if (m === 'solid') onChange({ type: 'solid', color: lastSolid })
          else
            onChange({
              type: 'gradient',
              stops: brand.length > 1 ? brand : [lastSolid, '#b4561f'],
              angle: palette.angle ?? 135,
            })
        }}
      />
      {fill?.type === 'solid' && (
        <ColorField label="Color" value={fill.color} onChange={(c) => onChange({ type: 'solid', color: c })} />
      )}
      {fill?.type === 'gradient' && (
        <>
          <div className="studio-gradient-preview" style={{ background: cssFill(fill) }} aria-hidden="true" />
          <GradientEditor
            stops={fill.stops}
            angle={fill.angle}
            onChange={(stops, angle) => onChange({ type: 'gradient', stops, angle })}
          />
        </>
      )}
    </div>
  )
}

export function ShadowEditor({ shadow, onChange }: { shadow: Shadow | null; onChange: (s: Shadow | null) => void }) {
  return (
    <div className="sf-stack">
      <Toggle label="Shadow" checked={!!shadow} onChange={(on) => onChange(on ? DEFAULT_SHADOW : null)} />
      {shadow && (
        <>
          <ColorField
            label="Shadow color"
            value={shadow.color}
            swatches={false}
            onChange={(color) => onChange({ ...shadow, color })}
          />
          <div className="sf-grid">
            <NumField
              label="Blur"
              value={shadow.blur}
              min={0}
              max={200}
              onChange={(blur) => onChange({ ...shadow, blur })}
            />
            <NumField
              label="Opacity"
              value={Math.round(shadow.opacity * 100)}
              min={0}
              max={100}
              onChange={(o) => onChange({ ...shadow, opacity: o / 100 })}
            />
            <NumField
              label="Offset X"
              value={shadow.offsetX}
              min={-200}
              max={200}
              onChange={(offsetX) => onChange({ ...shadow, offsetX })}
            />
            <NumField
              label="Offset Y"
              value={shadow.offsetY}
              min={-200}
              max={200}
              onChange={(offsetY) => onChange({ ...shadow, offsetY })}
            />
          </div>
        </>
      )}
    </div>
  )
}

export function Section({ title, children, open = true }: { title: string; children: ReactNode; open?: boolean }) {
  return (
    <details className="studio-section" open={open}>
      <summary>{title}</summary>
      <div className="studio-section-body">{children}</div>
    </details>
  )
}
