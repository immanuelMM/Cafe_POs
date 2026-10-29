import { useEffect, useRef, type ReactNode } from 'react'
import { IconX } from './icons'

interface Props {
  title: string
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
  wide?: boolean
  /**
   * Responsive by default: bottom sheet on phones, centred dialog on tablets.
   * 'side' = right-hand side sheet on tablets (admin edit forms); 'full' = full-screen on phones.
   */
  variant?: 'dialog' | 'side' | 'full'
}

/** Accessible dialog (role, label, Escape and focus return are the same for every variant). */
export function Modal({ title, onClose, children, footer, wide, variant = 'dialog' }: Props) {
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const prev = document.activeElement as HTMLElement | null
    ref.current?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', onKey)
    document.body.classList.add('no-scroll')
    return () => {
      document.removeEventListener('keydown', onKey)
      document.body.classList.remove('no-scroll')
      prev?.focus?.()
    }
  }, [onClose])

  const cls = `modal${wide ? ' modal-wide' : ''}${variant === 'dialog' ? '' : ` modal-${variant}`}`
  return (
    <div
      className={`modal-backdrop${variant === 'side' ? ' modal-backdrop-side' : ''}`}
      onMouseDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div className={cls} role="dialog" aria-modal="true" aria-label={title} tabIndex={-1} ref={ref}>
        <header className="modal-head">
          <h2>{title}</h2>
          <button type="button" className="btn btn-ghost btn-icon" onClick={onClose} aria-label="Close">
            <IconX />
          </button>
        </header>
        <div className="modal-body">{children}</div>
        {footer && <footer className="modal-foot">{footer}</footer>}
      </div>
    </div>
  )
}
