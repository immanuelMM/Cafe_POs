import { useEffect, useRef } from 'react'
import { parseDoc } from './factory'
import type { DesignKind, StudioDoc } from './types'

const key = (kind: DesignKind) => `cafe-studio-draft:${kind}`

export interface Draft {
  savedAt: number
  doc: StudioDoc
}

/** Local draft of unsaved work (localStorage; every access is guarded, storage may be full or blocked). */
export function readDraft(kind: DesignKind): Draft | null {
  try {
    const raw = localStorage.getItem(key(kind))
    if (!raw) return null
    const parsed = JSON.parse(raw) as { savedAt?: number; doc?: unknown }
    const doc = parseDoc(parsed.doc, kind)
    return doc && typeof parsed.savedAt === 'number' ? { savedAt: parsed.savedAt, doc } : null
  } catch {
    return null
  }
}

export function writeDraft(kind: DesignKind, doc: StudioDoc): boolean {
  try {
    localStorage.setItem(key(kind), JSON.stringify({ savedAt: Date.now(), doc }))
    return true
  } catch {
    return false
  }
}

export function clearDraft(kind: DesignKind) {
  try {
    localStorage.removeItem(key(kind))
  } catch {
    /* ignore */
  }
}

/** Debounced draft autosave while there are unsaved changes. `onResult(false)` when the browser refused the write. */
export function useDraftAutosave(
  kind: DesignKind,
  doc: StudioDoc,
  dirty: boolean,
  enabled: boolean,
  onResult: (ok: boolean) => void,
) {
  const cb = useRef(onResult)
  cb.current = onResult
  useEffect(() => {
    if (!dirty || !enabled) return
    const t = setTimeout(() => cb.current(writeDraft(kind, doc)), 1200)
    return () => clearTimeout(t)
  }, [kind, doc, dirty, enabled])
}

/** Warns before leaving with unsaved changes: tab close/reload, and in-app link clicks (the router has no blocker API here). */
export function useUnsavedGuard(dirty: boolean) {
  useEffect(() => {
    if (!dirty) return
    const onBeforeUnload = (e: BeforeUnloadEvent) => {
      e.preventDefault()
      e.returnValue = ''
    }
    const onClick = (e: MouseEvent) => {
      const a = (e.target as HTMLElement | null)?.closest?.('a[href]') as HTMLAnchorElement | null
      if (!a || a.target === '_blank' || a.hasAttribute('download') || a.origin !== window.location.origin) return
      if (a.pathname === window.location.pathname && a.search === window.location.search) return
      if (!window.confirm('You have unsaved changes in the design studio. Leave without saving?')) {
        e.preventDefault()
        e.stopPropagation()
      }
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    document.addEventListener('click', onClick, true)
    return () => {
      window.removeEventListener('beforeunload', onBeforeUnload)
      document.removeEventListener('click', onClick, true)
    }
  }, [dirty])
}
