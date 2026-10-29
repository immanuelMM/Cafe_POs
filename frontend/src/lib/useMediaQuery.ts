import { useSyncExternalStore } from 'react'

/** Subscribes to a CSS media query. Where matchMedia is missing (jsdom, SSR) it returns `fallback` (default: the wide layout). */
export function useMediaQuery(query: string, fallback = true): boolean {
  const mq = typeof window !== 'undefined' && typeof window.matchMedia === 'function' ? window.matchMedia(query) : null
  return useSyncExternalStore(
    (cb) => {
      mq?.addEventListener('change', cb)
      return () => mq?.removeEventListener('change', cb)
    },
    () => (mq ? mq.matches : fallback),
    () => fallback,
  )
}
