import { useMemo, useRef, useState } from 'react'
import { errorMessage } from '../../lib/errors'
import { ColorField, FillEditor, NumField, Section, Seg, ShadowEditor, Toggle } from './controls'
import { useStudioEnv } from './env'
import { fileToEmbedded, IMAGE_ACCEPT } from './images'
import { layoutMenu } from './menuLayout'
import { FONTS } from './presets'
import { PagePanel } from './PagePanel'
import { useStudio, type AlignMode, type Patch } from './store'
import type { ImageElement, MenuElement, ShapeElement, StudioElement, TextElement } from './types'

const ALIGNS: { mode: AlignMode; label: string; title: string }[] = [
  { mode: 'left', label: '⇤', title: 'Align left' },
  { mode: 'hcenter', label: '↔', title: 'Center horizontally' },
  { mode: 'right', label: '⇥', title: 'Align right' },
  { mode: 'top', label: '⤒', title: 'Align top' },
  { mode: 'vmiddle', label: '↕', title: 'Center vertically' },
  { mode: 'bottom', label: '⤓', title: 'Align bottom' },
]

type Upd = (patch: Patch, key?: string) => void

function FontSelect({ value, onChange }: { value: string; onChange: (stack: string) => void }) {
  const known = FONTS.some((f) => f.stack === value)
  return (
    <label className="field">
      <span className="field-label">Font</span>
      <select value={value} onChange={(e) => onChange(e.target.value)}>
        {!known && <option value={value}>Custom</option>}
        {FONTS.map((f) => (
          <option key={f.label} value={f.stack} style={{ fontFamily: f.stack }}>
            {f.label}
          </option>
        ))}
      </select>
    </label>
  )
}

function Outline({
  stroke,
  width,
  onChange,
  label = 'Outline',
}: {
  stroke: string | null
  width: number
  onChange: (stroke: string | null, width: number) => void
  label?: string
}) {
  return (
    <div className="sf-stack">
      <Toggle
        label={label}
        checked={!!stroke}
        onChange={(on) => onChange(on ? '#1f1410' : null, on ? Math.max(width, 3) : 0)}
      />
      {stroke && (
        <>
          <ColorField label={`${label} color`} value={stroke} onChange={(c) => onChange(c, width)} />
          <NumField label={`${label} width`} value={width} min={0} max={200} onChange={(w) => onChange(stroke, w)} />
        </>
      )}
    </div>
  )
}

function ShapeProps({ el, upd }: { el: ShapeElement; upd: Upd }) {
  const isLine = el.shape === 'line' || el.shape === 'arrow'
  return (
    <>
      {isLine ? (
        <Section title="Line">
          <ColorField label="Color" value={el.stroke ?? '#1f1410'} onChange={(stroke) => upd({ stroke })} />
          <NumField
            label="Thickness"
            value={el.strokeWidth}
            min={1}
            max={200}
            onChange={(w) => upd({ strokeWidth: w, height: Math.max(w, 1) }, `${el.id}:sw`)}
          />
        </Section>
      ) : (
        <>
          <Section title="Fill">
            <FillEditor label="Fill" fill={el.fill} allowNone onChange={(fill) => upd({ fill })} />
          </Section>
          <Section title="Outline" open={!!el.stroke}>
            <Outline
              stroke={el.stroke}
              width={el.strokeWidth}
              onChange={(stroke, strokeWidth) => upd({ stroke, strokeWidth })}
            />
          </Section>
        </>
      )}
      {(el.shape === 'roundRect' || el.shape === 'rect') && (
        <Section title="Corners">
          <NumField
            label="Corner radius"
            value={el.cornerRadius}
            min={0}
            max={1000}
            onChange={(cornerRadius) =>
              upd({ cornerRadius, shape: cornerRadius > 0 ? 'roundRect' : 'rect' }, `${el.id}:cr`)
            }
          />
        </Section>
      )}
      {el.shape === 'star' && (
        <Section title="Star">
          <NumField
            label="Points"
            value={el.points}
            min={3}
            max={16}
            onChange={(points) => upd({ points: Math.round(points) }, `${el.id}:pt`)}
          />
          <NumField
            label="Inner radius %"
            value={Math.round(el.innerRatio * 100)}
            min={10}
            max={90}
            onChange={(v) => upd({ innerRatio: v / 100 }, `${el.id}:ir`)}
          />
        </Section>
      )}
      {el.shape === 'polygon' && (
        <Section title="Polygon">
          <NumField
            label="Sides"
            value={el.sides}
            min={3}
            max={16}
            onChange={(sides) => upd({ sides: Math.round(sides) }, `${el.id}:sd`)}
          />
        </Section>
      )}
      {el.shape === 'ring' && (
        <Section title="Ring">
          <NumField
            label="Hole size %"
            value={Math.round(el.innerRatio * 100)}
            min={10}
            max={95}
            onChange={(v) => upd({ innerRatio: v / 100 }, `${el.id}:ir`)}
          />
        </Section>
      )}
      {!isLine && (
        <Section title="Flip" open={false}>
          <div className="row-actions">
            <button
              type="button"
              className="btn btn-sm"
              aria-pressed={el.flipX}
              onClick={() => upd({ flipX: !el.flipX })}
            >
              Flip horizontal
            </button>
            <button
              type="button"
              className="btn btn-sm"
              aria-pressed={el.flipY}
              onClick={() => upd({ flipY: !el.flipY })}
            >
              Flip vertical
            </button>
          </div>
        </Section>
      )}
      <Section title="Shadow" open={!!el.shadow}>
        <ShadowEditor shadow={el.shadow} onChange={(shadow) => upd({ shadow })} />
      </Section>
    </>
  )
}

function TextProps({ el, upd }: { el: TextElement; upd: Upd }) {
  const k = (f: string) => `${el.id}:${f}`
  return (
    <>
      <Section title="Text">
        <label className="field">
          <span className="field-label">Content</span>
          <textarea
            className="studio-textarea"
            rows={3}
            value={el.text}
            onChange={(e) => upd({ text: e.target.value }, k('text'))}
          />
        </label>
        <FontSelect value={el.fontFamily} onChange={(fontFamily) => upd({ fontFamily })} />
        <div className="sf-grid">
          <NumField
            label="Size"
            value={el.fontSize}
            min={4}
            max={1000}
            onChange={(fontSize) => upd({ fontSize }, k('fs'))}
          />
          <NumField
            label="Letter spacing"
            value={el.letterSpacing}
            min={-20}
            max={200}
            step={0.5}
            onChange={(letterSpacing) => upd({ letterSpacing }, k('ls'))}
          />
          <NumField
            label="Line height"
            value={el.lineHeight}
            min={0.6}
            max={3}
            step={0.05}
            onChange={(lineHeight) => upd({ lineHeight }, k('lh'))}
          />
        </div>
        <div className="row-actions">
          <button
            type="button"
            className="btn btn-sm studio-b"
            aria-pressed={el.bold}
            aria-label="Bold"
            onClick={() => upd({ bold: !el.bold })}
          >
            <b>B</b>
          </button>
          <button
            type="button"
            className="btn btn-sm studio-b"
            aria-pressed={el.italic}
            aria-label="Italic"
            onClick={() => upd({ italic: !el.italic })}
          >
            <i>I</i>
          </button>
          <button
            type="button"
            className="btn btn-sm studio-b"
            aria-pressed={el.underline}
            aria-label="Underline"
            onClick={() => upd({ underline: !el.underline })}
          >
            <u>U</u>
          </button>
        </div>
        <Seg
          label="Text alignment"
          value={el.align}
          options={[
            { value: 'left', label: 'Left' },
            { value: 'center', label: 'Center' },
            { value: 'right', label: 'Right' },
          ]}
          onChange={(align) => upd({ align })}
        />
        <label className="field">
          <span className="field-label">Curve ({el.curve})</span>
          <input
            type="range"
            min={-100}
            max={100}
            value={el.curve}
            onChange={(e) => upd({ curve: Number(e.target.value) }, k('curve'))}
          />
        </label>
      </Section>
      <Section title="Fill">
        <FillEditor label="Text fill" fill={el.fill} onChange={(fill) => fill && upd({ fill })} />
      </Section>
      <Section title="Outline" open={!!el.stroke}>
        <Outline
          stroke={el.stroke}
          width={el.strokeWidth}
          onChange={(stroke, strokeWidth) => upd({ stroke, strokeWidth })}
        />
      </Section>
      <Section title="Shadow" open={!!el.shadow}>
        <ShadowEditor shadow={el.shadow} onChange={(shadow) => upd({ shadow })} />
      </Section>
    </>
  )
}

function ImageProps({ el, upd }: { el: ImageElement; upd: Upd }) {
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | null>(null)
  const replace = async (f: File | undefined) => {
    if (input.current) input.current.value = ''
    if (!f) return
    try {
      setError(null)
      const img = await fileToEmbedded(f)
      upd({ src: img.src })
    } catch (e) {
      setError(errorMessage(e))
    }
  }
  return (
    <>
      <Section title="Image">
        {error && (
          <div className="alert alert-error" role="alert">
            {error}
          </div>
        )}
        <Seg
          label="Image fit"
          value={el.fit}
          options={[
            { value: 'cover', label: 'Fill', title: 'Cover the box, cropping the edges' },
            { value: 'contain', label: 'Fit', title: 'Show the whole image' },
            { value: 'fill', label: 'Stretch' },
          ]}
          onChange={(fit) => upd({ fit })}
        />
        <NumField
          label="Corner radius"
          value={el.cornerRadius}
          min={0}
          max={2000}
          onChange={(cornerRadius) => upd({ cornerRadius }, `${el.id}:cr`)}
        />
        <input
          ref={input}
          type="file"
          accept={IMAGE_ACCEPT}
          className="sr-only"
          id="studio-replace-image"
          onChange={(e) => void replace(e.target.files?.[0])}
        />
        <div className="row-actions">
          <label htmlFor="studio-replace-image" className="btn btn-sm">
            Replace image
          </label>
          <button
            type="button"
            className="btn btn-sm"
            aria-pressed={el.flipX}
            onClick={() => upd({ flipX: !el.flipX })}
          >
            Flip horizontal
          </button>
          <button
            type="button"
            className="btn btn-sm"
            aria-pressed={el.flipY}
            onClick={() => upd({ flipY: !el.flipY })}
          >
            Flip vertical
          </button>
        </div>
      </Section>
      <Section title="Outline" open={!!el.stroke}>
        <Outline
          stroke={el.stroke}
          width={el.strokeWidth}
          onChange={(stroke, strokeWidth) => upd({ stroke, strokeWidth })}
        />
      </Section>
      <Section title="Shadow" open={!!el.shadow}>
        <ShadowEditor shadow={el.shadow} onChange={(shadow) => upd({ shadow })} />
      </Section>
    </>
  )
}

function MenuProps({ el, upd }: { el: MenuElement; upd: Upd }) {
  const { sections } = useStudioEnv()
  const layout = useMemo(() => layoutMenu(el, sections), [el, sections])
  const k = (f: string) => `${el.id}:${f}`
  return (
    <Section title="Menu block">
      <p className="muted studio-note">Shows your live catalog (active items and prices). Resize the block to fit.</p>
      {layout.overflow && (
        <div className="alert alert-error" role="alert">
          The menu does not fit. Add columns, lower the font size or make the block bigger.
        </div>
      )}
      {layout.empty && <p className="muted studio-note">No active menu items yet.</p>}
      <Seg
        label="Columns"
        value={el.columns}
        options={[1, 2, 3, 4].map((n) => ({ value: n, label: `${n} col` }))}
        onChange={(columns) => upd({ columns })}
      />
      <FontSelect value={el.fontFamily} onChange={(fontFamily) => upd({ fontFamily })} />
      <Toggle label="Fit text to the block" checked={!!el.autoFit} onChange={(autoFit) => upd({ autoFit })} />
      {el.autoFit ? (
        <p className="muted studio-note">
          Using {Math.round(layout.fontSize)} px. Resize the block or change columns to scale the text.
        </p>
      ) : (
        <NumField
          label="Font size"
          value={el.fontSize}
          min={6}
          max={400}
          onChange={(fontSize) => upd({ fontSize }, k('fs'))}
        />
      )}
      <ColorField label="Text color" value={el.textColor} onChange={(textColor) => upd({ textColor })} />
      <ColorField label="Category color" value={el.accentColor} onChange={(accentColor) => upd({ accentColor })} />
      <ColorField label="Price color" value={el.priceColor} onChange={(priceColor) => upd({ priceColor })} />
      <Toggle
        label="Panel background"
        checked={!!el.background}
        onChange={(on) => upd({ background: on ? '#ffffff' : null })}
      />
      {el.background && (
        <ColorField label="Panel color" value={el.background} onChange={(background) => upd({ background })} />
      )}
      <Toggle label="Show photos" checked={el.showPhotos} onChange={(showPhotos) => upd({ showPhotos })} />
      <Toggle
        label="Show descriptions"
        checked={el.showDescriptions}
        onChange={(showDescriptions) => upd({ showDescriptions })}
      />
    </Section>
  )
}

function SingleProps({ el }: { el: StudioElement }) {
  const update = useStudio((s) => s.update)
  const remove = useStudio((s) => s.remove)
  const duplicate = useStudio((s) => s.duplicate)
  const align = useStudio((s) => s.align)
  const upd: Upd = (patch, key) => update([el.id], patch, key)
  const k = (f: string) => `${el.id}:${f}`
  const autoHeight = el.kind === 'text' || (el.kind === 'shape' && (el.shape === 'line' || el.shape === 'arrow'))
  return (
    <>
      <div className="studio-prop-head">
        <input
          aria-label="Layer name"
          className="studio-name"
          value={el.name}
          onChange={(e) => upd({ name: e.target.value }, k('name'))}
        />
        <span className="muted studio-kind">{el.kind === 'shape' ? el.shape : el.kind}</span>
      </div>
      <div className="studio-actions">
        <button type="button" className="btn btn-sm" onClick={() => duplicate([el.id])}>
          Duplicate
        </button>
        <button
          type="button"
          className="btn btn-sm"
          aria-pressed={el.locked}
          onClick={() => upd({ locked: !el.locked })}
        >
          {el.locked ? 'Unlock' : 'Lock'}
        </button>
        <button
          type="button"
          className="btn btn-sm"
          aria-pressed={el.hidden}
          onClick={() => upd({ hidden: !el.hidden })}
        >
          {el.hidden ? 'Show' : 'Hide'}
        </button>
        <button type="button" className="btn btn-sm btn-danger-ghost" onClick={() => remove([el.id])}>
          Delete
        </button>
      </div>
      {el.locked && <p className="muted studio-note">Locked: unlock it to move or edit.</p>}
      <Section title="Position and size">
        <div className="sf-grid">
          <NumField label="X" value={el.x} onChange={(x) => upd({ x }, k('x'))} />
          <NumField label="Y" value={el.y} onChange={(y) => upd({ y }, k('y'))} />
          <NumField label="Width" value={el.width} min={1} onChange={(width) => upd({ width }, k('w'))} />
          <NumField
            label="Height"
            value={el.height}
            min={1}
            disabled={autoHeight}
            onChange={(height) => upd({ height }, k('h'))}
          />
          <NumField
            label="Rotation"
            value={el.rotation}
            min={-360}
            max={360}
            onChange={(rotation) => upd({ rotation }, k('r'))}
          />
          <NumField
            label="Opacity %"
            value={Math.round(el.opacity * 100)}
            min={0}
            max={100}
            onChange={(o) => upd({ opacity: o / 100 }, k('o'))}
          />
        </div>
        <div className="studio-align" role="group" aria-label="Align to page">
          {ALIGNS.map((a) => (
            <button
              key={a.mode}
              type="button"
              className="btn btn-sm btn-icon"
              title={a.title}
              aria-label={a.title}
              onClick={() => align([el.id], a.mode)}
            >
              {a.label}
            </button>
          ))}
        </div>
      </Section>
      {el.kind === 'shape' && <ShapeProps el={el} upd={upd} />}
      {el.kind === 'text' && <TextProps el={el} upd={upd} />}
      {el.kind === 'image' && <ImageProps el={el} upd={upd} />}
      {el.kind === 'menu' && <MenuProps el={el} upd={upd} />}
    </>
  )
}

function MultiProps({ ids }: { ids: string[] }) {
  const { update, remove, duplicate, align, order } = useStudio.getState()
  const all = useStudio((s) => s.doc.elements)
  const els = useMemo(() => all.filter((e) => ids.includes(e.id)), [all, ids])
  return (
    <>
      <div className="studio-prop-head">
        <strong>{ids.length} elements selected</strong>
      </div>
      <div className="studio-actions">
        <button type="button" className="btn btn-sm" onClick={() => duplicate(ids)}>
          Duplicate
        </button>
        <button type="button" className="btn btn-sm" onClick={() => update(ids, { locked: true })}>
          Lock
        </button>
        <button type="button" className="btn btn-sm" onClick={() => update(ids, { hidden: true })}>
          Hide
        </button>
        <button type="button" className="btn btn-sm btn-danger-ghost" onClick={() => remove(ids)}>
          Delete
        </button>
      </div>
      <Section title="Align">
        <div className="studio-align" role="group" aria-label="Align selection">
          {ALIGNS.map((a) => (
            <button
              key={a.mode}
              type="button"
              className="btn btn-sm btn-icon"
              title={a.title}
              aria-label={a.title}
              onClick={() => align(ids, a.mode)}
            >
              {a.label}
            </button>
          ))}
        </div>
        <div className="row-actions">
          <button type="button" className="btn btn-sm" onClick={() => order(ids, 'front')}>
            To front
          </button>
          <button type="button" className="btn btn-sm" onClick={() => order(ids, 'back')}>
            To back
          </button>
        </div>
        <NumField
          label="Opacity %"
          value={Math.round((els[0]?.opacity ?? 1) * 100)}
          min={0}
          max={100}
          onChange={(o) => update(ids, { opacity: o / 100 }, 'multi:o')}
        />
      </Section>
      <p className="muted studio-note">Drag to move them together. Hold Shift to resize proportionally.</p>
    </>
  )
}

/** Right-hand panel: properties of the selection, or the page settings when nothing is selected. */
export function PropertiesPanel({ onTemplates }: { onTemplates: () => void }) {
  const selected = useStudio((s) => s.selected)
  const els = useStudio((s) => s.doc.elements)
  const live = selected.filter((id) => els.some((e) => e.id === id))
  if (live.length === 0) return <PagePanel onTemplates={onTemplates} />
  if (live.length > 1) return <MultiProps ids={live} />
  const el = els.find((e) => e.id === live[0])
  return el ? <SingleProps key={el.id} el={el} /> : null
}
