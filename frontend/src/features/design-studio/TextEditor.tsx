import { useEffect, useRef, useState } from 'react'
import { onColor } from '../../lib/color'
import type { TextElement } from './types'

/** Inline text editing: a textarea laid over the canvas node (double-click / double-tap a text element). */
export function TextEditor({
  el,
  scale,
  onCommit,
  onCancel,
}: {
  el: TextElement
  scale: number
  onCommit: (text: string) => void
  onCancel: () => void
}) {
  const ref = useRef<HTMLTextAreaElement>(null)
  const [value, setValue] = useState(el.text)
  const done = useRef(false)
  const color = el.fill.type === 'solid' ? el.fill.color : (el.fill.stops[0] ?? '#000000')

  const grow = () => {
    const t = ref.current
    if (!t) return
    t.style.height = '0px'
    t.style.height = `${t.scrollHeight}px`
  }
  useEffect(() => {
    const t = ref.current
    if (!t) return
    t.focus()
    t.select()
    grow()
  }, [])

  const finish = (commit: boolean) => {
    if (done.current) return
    done.current = true
    if (commit) onCommit(value)
    else onCancel()
  }

  return (
    <textarea
      ref={ref}
      className="studio-text-edit"
      aria-label="Edit text"
      value={value}
      spellCheck={false}
      onChange={(e) => {
        setValue(e.target.value)
        grow()
      }}
      onBlur={() => finish(true)}
      onKeyDown={(e) => {
        e.stopPropagation()
        if (e.key === 'Escape') finish(false)
        if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) finish(true)
      }}
      style={{
        left: el.x * scale,
        top: el.y * scale,
        width: Math.max(el.width * scale, 40),
        transform: `rotate(${el.rotation}deg)`,
        fontFamily: el.fontFamily,
        fontSize: el.fontSize * scale,
        fontWeight: el.bold ? 700 : 400,
        fontStyle: el.italic ? 'italic' : 'normal',
        textDecoration: el.underline ? 'underline' : 'none',
        lineHeight: el.lineHeight,
        letterSpacing: el.letterSpacing * scale,
        textAlign: el.align,
        color,
        background: onColor(color) === '#ffffff' ? 'rgba(255,255,255,0.88)' : 'rgba(31,20,16,0.82)',
      }}
    />
  )
}
