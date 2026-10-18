import { useEffect, useRef, useState } from 'react'
import { Field } from '../../components/ui'
import { errorMessage } from '../../lib/api'
import { brandBackground, paletteStops, type BrandPalette } from '../../lib/color'
import { FONTS, ICONS, LAYOUTS, SHAPES, drawLogo, loadLogoSettings, renderLogoBlob, saveLogoSettings, type LogoSettings } from './logoDraw'

const MAX_LOGO_BYTES = 1024 * 1024

function LogoCanvas({ settings, size, palette }: { settings: LogoSettings; size: number; palette: BrandPalette }) {
  const ref = useRef<HTMLCanvasElement>(null)
  useEffect(() => {
    const c = ref.current
    const ctx = c?.getContext('2d')
    if (!c || !ctx) return
    const dpr = window.devicePixelRatio || 1
    c.width = c.height = Math.round(size * dpr)
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
    drawLogo(ctx, size, settings, palette)
  }, [settings, size, palette])
  return <canvas ref={ref} className="logo-canvas" style={{ width: size, height: size }} aria-hidden="true" />
}

function ColorField({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <Field label={label}>
      <input type="color" className="color-input" value={value} onChange={(e) => onChange(e.target.value)} />
    </Field>
  )
}

interface Props {
  /** Brand colours: the primary is the default background; 2+ colours enable the gradient background option. */
  palette: BrandPalette
  busy: boolean
  error: unknown
  onUse: (png: Blob) => void
}

/** Dependency-free logo maker: icon + text on a shape, drawn with Canvas 2D and exported as a 512x512 PNG. */
export function LogoMaker({ palette, busy, error, onUse }: Props) {
  const [s, setS] = useState<LogoSettings>(() => loadLogoSettings(palette.primary))
  const [localError, setLocalError] = useState<string | null>(null)
  const set = <K extends keyof LogoSettings>(k: K, v: LogoSettings[K]) => setS((p) => ({ ...p, [k]: v }))

  // Remember the last settings so the user can come back and tweak.
  useEffect(() => saveLogoSettings(s), [s])

  const use = async () => {
    setLocalError(null)
    try {
      const blob = await renderLogoBlob(s, 512, palette)
      if (blob.size > MAX_LOGO_BYTES) throw new Error('The generated logo is larger than 1 MB. Try a simpler design.')
      onUse(blob)
    } catch (e) {
      setLocalError(errorMessage(e))
    }
  }

  const canGradient = paletteStops(palette).length > 1
  const fill = s.bgStyle === 'gradient' && canGradient ? 'gradient' : 'solid'
  const shownError = localError ?? (error ? errorMessage(error) : null)
  return (
    <div className="logo-maker">
      <div className="logo-previews" aria-label="Preview">
        <figure>
          <div className="logo-preview-bar" style={{ background: brandBackground(palette) }}>
            <LogoCanvas settings={s} size={32} palette={palette} />
          </div>
          <figcaption>Header</figcaption>
        </figure>
        <figure>
          <LogoCanvas settings={s} size={96} palette={palette} />
          <figcaption>Login</figcaption>
        </figure>
        <figure>
          <LogoCanvas settings={s} size={256} palette={palette} />
          <figcaption>Banner</figcaption>
        </figure>
      </div>

      <div className="form">
        <fieldset className="opt-group">
          <legend>Icon</legend>
          <div className="chips swatch-row" role="radiogroup" aria-label="Icon">
            {ICONS.map((i) => (
              <button
                key={i.id}
                type="button"
                role="radio"
                aria-checked={s.icon === i.id}
                className={s.icon === i.id ? 'tab tab-on' : 'tab'}
                onClick={() => set('icon', i.id)}
              >
                {i.label}
              </button>
            ))}
          </div>
          <div className="chips swatch-row" role="radiogroup" aria-label="Layout">
            {LAYOUTS.map((l) => (
              <button
                key={l.id}
                type="button"
                role="radio"
                aria-checked={s.layout === l.id}
                className={s.layout === l.id ? 'tab tab-on' : 'tab'}
                onClick={() => set('layout', l.id)}
              >
                {l.label}
              </button>
            ))}
          </div>
        </fieldset>

        <div className="grid-2">
          <Field label="Text (initials or short name)">
            <input value={s.text} maxLength={12} onChange={(e) => set('text', e.target.value)} />
          </Field>
          <Field label="Font">
            <select value={s.font} onChange={(e) => set('font', e.target.value as LogoSettings['font'])}>
              {FONTS.map((f) => (
                <option key={f.id} value={f.id}>
                  {f.label}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Background shape">
            <select value={s.shape} onChange={(e) => set('shape', e.target.value as LogoSettings['shape'])}>
              {SHAPES.map((x) => (
                <option key={x.id} value={x.id}>
                  {x.label}
                </option>
              ))}
            </select>
          </Field>
          <label className="check">
            <input type="checkbox" checked={s.ring} disabled={s.shape === 'none'} onChange={(e) => set('ring', e.target.checked)} /> Border ring
          </label>
        </div>
        <fieldset className="opt-group">
          <legend>Background fill</legend>
          <div className="chips swatch-row" role="radiogroup" aria-label="Background fill">
            {(['solid', 'gradient'] as const).map((id) => (
              <button
                key={id}
                type="button"
                role="radio"
                aria-checked={fill === id}
                disabled={id === 'gradient' && !canGradient}
                className={fill === id ? 'tab tab-on' : 'tab'}
                onClick={() => set('bgStyle', id)}
              >
                {id === 'solid' ? 'Solid' : 'Brand gradient'}
              </button>
            ))}
          </div>
          {!canGradient && <small className="muted">Add a second brand colour in the Brand card to use a gradient.</small>}
        </fieldset>
        <div className="color-row">
          <ColorField label="Text and icon" value={s.textColor} onChange={(v) => set('textColor', v)} />
          {fill === 'solid' && <ColorField label="Background" value={s.bgColor} onChange={(v) => set('bgColor', v)} />}
          <ColorField label="Ring" value={s.ringColor} onChange={(v) => set('ringColor', v)} />
          <button type="button" className="btn btn-sm" onClick={() => set('bgColor', palette.primary)}>
            Use brand colour
          </button>
        </div>
        {shownError && <div className="alert alert-error" role="alert">{shownError}</div>}
        <button type="button" className="btn btn-primary" disabled={busy} onClick={() => void use()}>
          {busy ? 'Uploading…' : 'Use this logo'}
        </button>
      </div>
    </div>
  )
}
