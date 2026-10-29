import { useRef, useState } from 'react'
import { errorMessage } from '../../lib/api'
import { paletteStops } from '../../lib/color'
import { ColorField, GradientEditor, Section, Seg, Toggle } from './controls'
import { useStudioEnv } from './env'
import { fileToEmbedded, IMAGE_ACCEPT } from './images'
import { findPreset, presetsFor } from './presets'
import { useStudio } from './store'
import type { Background } from './types'

/** Canvas size and page background (shown when nothing is selected). */
export function PagePanel({ onTemplates }: { onTemplates: () => void }) {
  const doc = useStudio((s) => s.doc)
  const setBackground = useStudio((s) => s.setBackground)
  const resize = useStudio((s) => s.resize)
  const { palette } = useStudioEnv()
  const [scaleContent, setScaleContent] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const input = useRef<HTMLInputElement>(null)
  const bg = doc.background
  const presets = presetsFor(doc.kind)
  const brand = paletteStops(palette)

  const switchTo = (type: Background['type']) => {
    const color = bg.type === 'solid' ? bg.color : bg.type === 'image' ? bg.color : (brand[0] ?? '#ffffff')
    if (type === 'solid') setBackground({ type: 'solid', color: bg.type === 'gradient' ? bg.stops[0] : color })
    else if (type === 'gradient')
      setBackground({
        type: 'gradient',
        stops: brand.length > 1 ? brand : [color, '#b4561f'],
        angle: palette.angle ?? 135,
      })
    else if (type === 'transparent') setBackground({ type: 'transparent' })
    else input.current?.click()
  }
  const pickImage = async (f: File | undefined) => {
    if (input.current) input.current.value = ''
    if (!f) return
    try {
      setError(null)
      const img = await fileToEmbedded(f)
      setBackground({ type: 'image', src: img.src, fit: 'cover', color: bg.type === 'solid' ? bg.color : '#ffffff' })
    } catch (e) {
      setError(errorMessage(e))
    }
  }
  const options: { value: Background['type']; label: string }[] = [
    { value: 'solid', label: 'Solid' },
    { value: 'gradient', label: 'Gradient' },
    { value: 'image', label: 'Image' },
    ...(doc.kind === 'logo' ? [{ value: 'transparent' as const, label: 'None' }] : []),
  ]

  return (
    <>
      <div className="studio-prop-head">
        <strong>Page</strong>
        <span className="muted studio-kind">
          {doc.width} x {doc.height}
        </span>
      </div>
      <Section title="Canvas size">
        <label className="field">
          <span className="field-label">Preset</span>
          <select
            value={findPreset(doc.presetId) ? doc.presetId : ''}
            onChange={(e) => {
              const p = findPreset(e.target.value)
              if (p) resize(p, scaleContent)
            }}
          >
            {!findPreset(doc.presetId) && <option value="">Custom</option>}
            {presets.map((p) => (
              <option key={p.id} value={p.id}>
                {p.label}
              </option>
            ))}
          </select>
        </label>
        <Toggle label="Scale content to the new size" checked={scaleContent} onChange={setScaleContent} />
      </Section>
      <Section title="Background">
        {error && (
          <div className="alert alert-error" role="alert">
            {error}
          </div>
        )}
        <Seg label="Background type" value={bg.type} options={options} onChange={switchTo} />
        <input
          ref={input}
          type="file"
          accept={IMAGE_ACCEPT}
          className="sr-only"
          aria-label="Background image file"
          onChange={(e) => void pickImage(e.target.files?.[0])}
        />
        {bg.type === 'solid' && (
          <ColorField
            label="Color"
            value={bg.color}
            onChange={(color) => setBackground({ type: 'solid', color }, 'bg')}
          />
        )}
        {bg.type === 'gradient' && (
          <GradientEditor
            stops={bg.stops}
            angle={bg.angle}
            onChange={(stops, angle) => setBackground({ type: 'gradient', stops, angle }, 'bg')}
          />
        )}
        {bg.type === 'image' && (
          <>
            <Seg
              label="Image fit"
              value={bg.fit}
              options={[
                { value: 'cover', label: 'Cover' },
                { value: 'contain', label: 'Contain' },
              ]}
              onChange={(fit) => setBackground({ ...bg, fit })}
            />
            <ColorField
              label="Behind the image"
              value={bg.color}
              onChange={(color) => setBackground({ ...bg, color }, 'bg')}
            />
            <button type="button" className="btn btn-sm" onClick={() => input.current?.click()}>
              Choose another image
            </button>
          </>
        )}
        {bg.type === 'transparent' && (
          <p className="muted studio-note">The logo keeps a transparent background (PNG).</p>
        )}
      </Section>
      <Section title="Starter">
        <button type="button" className="btn btn-sm" onClick={onTemplates}>
          Choose a template
        </button>
      </Section>
    </>
  )
}
