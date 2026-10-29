import { useCallback, useEffect, useState } from 'react'
import { Modal } from '../../components/Modal'

const MIN = 4
const MAX = 6

interface Props {
  title: string
  /** Shown above the dots, e.g. the staff member's name. */
  subtitle?: string
  busy?: boolean
  /** Server error from the last attempt; the PIN field is cleared on every submit. */
  error?: string | null
  /** Ask twice (used when choosing a new PIN). */
  confirm?: boolean
  onSubmit: (pin: string) => void
  onClose: () => void
}

/** Touch-friendly PIN pad. Also accepts keyboard digits, Backspace and Enter. The PIN is always masked. */
export function PinModal({ title, subtitle, busy, error, confirm, onSubmit, onClose }: Props) {
  const [pin, setPin] = useState('')
  const [first, setFirst] = useState<string | null>(null)
  const [localError, setLocalError] = useState<string | null>(null)

  const add = useCallback((d: string) => !busy && setPin((p) => (p.length < MAX ? p + d : p)), [busy])
  const back = useCallback(() => !busy && setPin((p) => p.slice(0, -1)), [busy])
  const ok = useCallback(() => {
    if (busy || pin.length < MIN) return
    if (confirm && first === null) {
      setFirst(pin)
      setPin('')
      setLocalError(null)
    } else if (confirm && first !== pin) {
      setFirst(null)
      setPin('')
      setLocalError('PINs did not match. Start again.')
    } else {
      onSubmit(pin)
      setPin('')
    }
  }, [busy, pin, confirm, first, onSubmit])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (/^\d$/.test(e.key)) add(e.key)
      else if (e.key === 'Backspace') back()
      else if (e.key === 'Enter') {
        e.preventDefault()
        ok()
      }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [add, back, ok])

  const shownError = localError ?? error
  const prompt = confirm
    ? first === null
      ? 'Choose a PIN (4 to 6 digits)'
      : 'Enter it again to confirm'
    : 'Enter your PIN'
  return (
    <Modal title={title} onClose={onClose}>
      {subtitle && <p className="pin-who">{subtitle}</p>}
      <p className="muted center" style={{ margin: 0 }}>
        {prompt}
      </p>
      <div className="pin-dots" role="status" aria-label={`${pin.length} digits entered`}>
        {Array.from({ length: MAX }, (_, i) => (
          <span key={i} className={i < pin.length ? 'pin-dot pin-dot-on' : 'pin-dot'} aria-hidden="true" />
        ))}
      </div>
      <div className="pin-msg" role="alert">
        {shownError}
      </div>
      <div className="pinpad">
        {['1', '2', '3', '4', '5', '6', '7', '8', '9'].map((d) => (
          <button key={d} type="button" className="btn pin-key" onClick={() => add(d)} disabled={busy}>
            {d}
          </button>
        ))}
        <button type="button" className="btn pin-key" onClick={back} disabled={busy || !pin} aria-label="Backspace">
          ⌫
        </button>
        <button type="button" className="btn pin-key" onClick={() => add('0')} disabled={busy}>
          0
        </button>
        <button type="button" className="btn btn-primary pin-key" onClick={ok} disabled={busy || pin.length < MIN}>
          {busy ? '…' : 'OK'}
        </button>
      </div>
    </Modal>
  )
}
