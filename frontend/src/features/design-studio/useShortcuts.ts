import { useEffect, useRef } from 'react'
import { useStudio } from './store'

const isTyping = (t: EventTarget | null) => {
  const el = t as HTMLElement | null
  if (!el) return false
  return el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT' || el.isContentEditable
}

/** Editor keyboard shortcuts and system-clipboard paste. Ignored while typing in a field. */
export function useShortcuts({ onSave, onPasteFiles }: { onSave: () => void; onPasteFiles: (files: File[]) => void }) {
  const saveRef = useRef(onSave)
  saveRef.current = onSave
  const filesRef = useRef(onPasteFiles)
  filesRef.current = onPasteFiles

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const mod = e.ctrlKey || e.metaKey
      const k = e.key.toLowerCase()
      if (mod && k === 's') {
        e.preventDefault()
        saveRef.current()
        return
      }
      if (isTyping(e.target)) return
      const st = useStudio.getState()
      const sel = st.selected
      if (mod && k === 'z') {
        e.preventDefault()
        if (e.shiftKey) st.redo()
        else st.undo()
      } else if (mod && k === 'y') {
        e.preventDefault()
        st.redo()
      } else if (mod && k === 'c') {
        if (sel.length) {
          e.preventDefault()
          st.copy(sel)
        }
      } else if (mod && k === 'd') {
        e.preventDefault()
        if (sel.length) st.duplicate(sel)
      } else if (mod && k === 'a') {
        e.preventDefault()
        st.selectAll()
      } else if (!mod && (e.key === 'Delete' || e.key === 'Backspace')) {
        if (sel.length) {
          e.preventDefault()
          st.remove(sel.filter((id) => st.doc.elements.some((el) => el.id === id && !el.locked)))
        }
      } else if (!mod && e.key.startsWith('Arrow')) {
        if (!sel.length) return
        e.preventDefault()
        const d = e.shiftKey ? 10 : 1
        st.nudge(sel, e.key === 'ArrowLeft' ? -d : e.key === 'ArrowRight' ? d : 0, e.key === 'ArrowUp' ? -d : e.key === 'ArrowDown' ? d : 0)
      } else if (e.key === 'Escape') {
        st.select([])
      }
    }
    const onPaste = (e: ClipboardEvent) => {
      if (isTyping(e.target)) return
      const files = Array.from(e.clipboardData?.files ?? []).filter((f) => f.type.startsWith('image/'))
      e.preventDefault()
      if (files.length) filesRef.current(files)
      else useStudio.getState().paste()
    }
    window.addEventListener('keydown', onKey)
    window.addEventListener('paste', onPaste)
    return () => {
      window.removeEventListener('keydown', onKey)
      window.removeEventListener('paste', onPaste)
    }
  }, [])
}
