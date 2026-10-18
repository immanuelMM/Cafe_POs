import { useState } from 'react'
import { useStudio } from './store'
import type { StudioElement } from './types'

const kindLabel = (e: StudioElement) => (e.kind === 'shape' ? (e.shape === 'icon' ? 'icon' : e.shape) : e.kind)

/** Layer list (top of the list = front of the canvas): select, rename, reorder, lock, hide, delete. */
export function LayersPanel() {
  const elements = useStudio((s) => s.doc.elements)
  const selected = useStudio((s) => s.selected)
  const { select, toggleSelect, update, remove, order } = useStudio.getState()
  const [renaming, setRenaming] = useState<string | null>(null)
  const rows = [...elements].reverse()

  if (!elements.length) return <p className="muted studio-note">No layers yet. Add text, shapes or images from the Add tab.</p>

  return (
    <>
      <div className="studio-actions" role="group" aria-label="Arrange selection">
        {(
          [
            ['front', 'To front'],
            ['forward', 'Forward'],
            ['backward', 'Backward'],
            ['back', 'To back'],
          ] as const
        ).map(([mode, label]) => (
          <button key={mode} type="button" className="btn btn-sm" disabled={!selected.length} onClick={() => order(selected, mode)}>
            {label}
          </button>
        ))}
      </div>
      <ul className="studio-layers" aria-label="Layers">
        {rows.map((e) => {
          const on = selected.includes(e.id)
          return (
            <li key={e.id} className={`studio-layer${on ? ' studio-layer-on' : ''}${e.hidden ? ' studio-layer-off' : ''}`}>
              {renaming === e.id ? (
                <input
                  autoFocus
                  className="studio-layer-input"
                  aria-label="Layer name"
                  defaultValue={e.name}
                  onFocus={(ev) => ev.currentTarget.select()}
                  onBlur={(ev) => {
                    update([e.id], { name: ev.target.value.trim() || e.name })
                    setRenaming(null)
                  }}
                  onKeyDown={(ev) => {
                    if (ev.key === 'Enter') ev.currentTarget.blur()
                    if (ev.key === 'Escape') setRenaming(null)
                  }}
                />
              ) : (
                <button
                  type="button"
                  className="studio-layer-main"
                  aria-pressed={on}
                  title="Click to select, double-click to rename"
                  onClick={(ev) => (ev.shiftKey || ev.ctrlKey || ev.metaKey ? toggleSelect(e.id) : select([e.id]))}
                  onDoubleClick={() => setRenaming(e.id)}
                >
                  <span className="studio-layer-name">{e.name}</span>
                  <small className="muted">{kindLabel(e)}</small>
                </button>
              )}
              <button type="button" className="btn btn-ghost btn-icon studio-mini" title="Bring forward" aria-label={`Bring ${e.name} forward`} onClick={() => order([e.id], 'forward')}>
                ▲
              </button>
              <button type="button" className="btn btn-ghost btn-icon studio-mini" title="Send backward" aria-label={`Send ${e.name} backward`} onClick={() => order([e.id], 'backward')}>
                ▼
              </button>
              <button type="button" className="btn btn-ghost btn-icon studio-mini" title={e.hidden ? 'Show' : 'Hide'} aria-label={`${e.hidden ? 'Show' : 'Hide'} ${e.name}`} aria-pressed={e.hidden} onClick={() => update([e.id], { hidden: !e.hidden })}>
                {e.hidden ? '○' : '◉'}
              </button>
              <button type="button" className="btn btn-ghost btn-icon studio-mini" title={e.locked ? 'Unlock' : 'Lock'} aria-label={`${e.locked ? 'Unlock' : 'Lock'} ${e.name}`} aria-pressed={e.locked} onClick={() => update([e.id], { locked: !e.locked })}>
                {e.locked ? '▣' : '□'}
              </button>
              <button type="button" className="btn btn-ghost btn-icon studio-mini" title="Delete" aria-label={`Delete ${e.name}`} onClick={() => remove([e.id])}>
                ✕
              </button>
            </li>
          )
        })}
      </ul>
    </>
  )
}
