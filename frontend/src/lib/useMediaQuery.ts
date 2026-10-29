import { useCallback, useMemo, useSyncExternalStore } from 'react'

/** Subscribes to a CSS media query. Where matchMedia is missing (jsdom, SSR) it returns `fallback` (default: the wide layout). */
export function useMediaQuery(query: string, fallback = true): boolean {
  // One MediaQueryList and one subscribe function per query, so React does not resubscribe on every render.
  const mq = useMemo(
    () => (typeof window !== 'undefined' && typeof window.matchMedia === 'function' ? window.matchMedia(query) : null),
    [query],
  )
  const subscribe = useCallback(
    (cb: () => void) => {
      mq?.addEventListener('change', cb)
      return () => mq?.removeEventListener('change', cb)
    },
    [mq],
  )
  return useSyncExternalStore(
    subscribe,
    () => (mq ? mq.matches : fallback),
    () => fallback,
  )
}
