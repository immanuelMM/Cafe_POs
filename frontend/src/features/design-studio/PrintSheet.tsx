import { useEffect } from 'react'
import { createPortal } from 'react-dom'

/**
 * Prints the exported banner image full-page at the preset's physical size. Rendered into document.body (outside #root)
 * and activated with body.print-studio, so receipt printing (#print-root) and the menu banner page are not affected.
 */
export function PrintSheet({ url, size, onDone }: { url: string; size: { w: number; h: number; unit: 'in' | 'mm' }; onDone: () => void }) {
  useEffect(() => {
    let cancelled = false
    document.body.classList.add('print-studio')
    const finish = () => onDone()
    window.addEventListener('afterprint', finish, { once: true })
    const pre = new Image()
    pre.src = url
    void pre
      .decode()
      .catch(() => undefined)
      .then(() => {
        if (!cancelled) window.print()
      })
    return () => {
      cancelled = true
      window.removeEventListener('afterprint', finish)
      document.body.classList.remove('print-studio')
    }
  }, [url, onDone])

  return createPortal(
    <div id="print-studio-root">
      <style>{`@media print { @page { size: ${size.w}${size.unit} ${size.h}${size.unit}; margin: 0; } }`}</style>
      <img src={url} alt="" />
    </div>,
    document.body,
  )
}
