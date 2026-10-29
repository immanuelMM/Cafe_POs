import { useEffect, useState } from 'react'
import { useMediaQuery } from '../lib/useMediaQuery'

/**
 * Full-screen toggle. `available` is false when the Fullscreen API is missing (iPhone Safari) or the app already runs
 * as an installed PWA (standalone / fullscreen display mode), where the toggle would be pointless.
 */
export function useFullscreen() {
  const [on, setOn] = useState(!!document.fullscreenElement)
  const installed = useMediaQuery('(display-mode: standalone), (display-mode: fullscreen)', false)
  useEffect(() => {
    const h = () => setOn(!!document.fullscreenElement)
    document.addEventListener('fullscreenchange', h)
    return () => document.removeEventListener('fullscreenchange', h)
  }, [])
  const toggle = () => {
    if (document.fullscreenElement) void document.exitFullscreen()
    else void document.documentElement.requestFullscreen?.().catch(() => undefined)
  }
  return { on, toggle, available: !installed && !!document.documentElement.requestFullscreen }
}
