import { useMemo } from 'react'
import { Modal } from '../../components/Modal'
import { cssFill } from './geometry'
import { findPreset, defaultPreset } from './presets'
import { templatesFor, type TemplateCtx, type TemplateDef } from './templates'
import type { DesignKind, StudioDoc, StudioElement } from './types'

/** Lightweight CSS preview of a document (percent boxes; text sized with container-query units). */
function MiniPreview({ doc }: { doc: StudioDoc }) {
  const pct = (v: number, of: number) => `${(v / of) * 100}%`
  const bg = doc.background
  const background = bg.type === 'solid' ? bg.color : bg.type === 'gradient' ? cssFill(bg) : undefined
  const box = (e: StudioElement) => ({ left: pct(e.x, doc.width), top: pct(e.y, doc.height), width: pct(e.width, doc.width), height: pct(e.height, doc.height) })
  return (
    <div className={`studio-mini-doc${bg.type === 'transparent' ? ' studio-checker' : ''}`} style={{ aspectRatio: `${doc.width} / ${doc.height}`, background }} aria-hidden="true">
      {doc.elements.map((e) => {
        if (e.kind === 'text')
          return (
            <span key={e.id} className="studio-mini-el" style={{ ...box(e), fontSize: `${(e.fontSize / doc.width) * 100}cqw`, color: e.fill.type === 'solid' ? e.fill.color : e.fill.stops[0], textAlign: e.align, fontFamily: e.fontFamily, fontWeight: e.bold ? 700 : 400, lineHeight: 1.1, height: 'auto' }}>
              {e.text}
            </span>
          )
        if (e.kind === 'menu')
          return <span key={e.id} className="studio-mini-el studio-mini-menu" style={box(e)} />
        if (e.kind === 'image') return <span key={e.id} className="studio-mini-el studio-mini-img" style={box(e)} />
        const round = e.shape === 'circle' || e.shape === 'ellipse' || e.shape === 'ring'
        return (
          <span
            key={e.id}
            className="studio-mini-el"
            style={{
              ...box(e),
              background: e.shape === 'icon' ? undefined : cssFill(e.fill),
              color: e.fill?.type === 'solid' ? e.fill.color : undefined,
              border: e.stroke ? `${Math.max(1, (e.strokeWidth / doc.width) * 100)}cqw solid ${e.stroke}` : undefined,
              borderRadius: round ? '50%' : e.shape === 'roundRect' ? `${(e.cornerRadius / e.width) * 100}%` : undefined,
            }}
          >
            {e.shape === 'icon' && '☕'}
          </span>
        )
      })}
    </div>
  )
}

interface Props {
  kind: DesignKind
  presetId: string
  ctx: Omit<TemplateCtx, 'preset'>
  hasContent: boolean
  onPick: (doc: StudioDoc) => void
  onBlank: () => void
  onClose: () => void
}

export function TemplatePicker({ kind, presetId, ctx, hasContent, onPick, onBlank, onClose }: Props) {
  const preset = findPreset(presetId) ?? defaultPreset(kind)
  const templates = templatesFor(kind)
  const built = useMemo(() => templates.map((t) => [t, t.build({ ...ctx, preset })] as [TemplateDef, StudioDoc]), [templates, ctx, preset])
  return (
    <Modal title="Start from a template" onClose={onClose} wide>
      {hasContent && <p className="muted">Applying a template replaces the current design. You can undo it.</p>}
      <ul className="studio-templates">
        {built.map(([t, doc]) => (
          <li key={t.id}>
            <button type="button" className="studio-template" onClick={() => onPick(doc)}>
              <MiniPreview doc={doc} />
              <strong>{t.name}</strong>
              <small className="muted">{t.description}</small>
            </button>
          </li>
        ))}
        <li>
          <button type="button" className="studio-template" onClick={onBlank}>
            <div className="studio-mini-doc studio-mini-blank" style={{ aspectRatio: `${preset.width} / ${preset.height}` }} aria-hidden="true" />
            <strong>Blank canvas</strong>
            <small className="muted">Start from scratch</small>
          </button>
        </li>
      </ul>
    </Modal>
  )
}
