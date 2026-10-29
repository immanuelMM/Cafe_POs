import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Keypad, type KeypadKey } from '../../components/Keypad'
import { Modal } from '../../components/Modal'
import { useMediaQuery } from '../../lib/useMediaQuery'
import { ApiError, errorMessage } from '../../lib/errors'
import { formatMoney, round2 } from '../../lib/format'
import type { Order } from '../../api/types'
import { PrintableReceipt } from '../orders/ReceiptView'
import { orderCalls } from '../orders/useOrders'
import { cartTotal, useCartStore } from './cartStore'

/** Quick tender buttons: exact, then the next common bill denominations above the total. */
function quickAmounts(total: number): number[] {
  const out = new Set<number>([total])
  for (const d of [50, 100, 500, 1000]) {
    const v = Math.ceil(total / d) * d
    if (v >= total) out.add(v)
  }
  return [...out].sort((a, b) => a - b).slice(0, 4)
}

/** Whole-peso label for a quick-tender note, e.g. ₱500 or ₱1,000 (short enough for four across on a phone). */
const tenderLabel = (a: number) => `₱${a.toLocaleString('en-PH', { maximumFractionDigits: 2 })}`

export function PaymentPanel({ onClose }: { onClose: () => void }) {
  const lines = useCartStore((s) => s.lines)
  const total = cartTotal(lines)
  const [tendered, setTendered] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState<Order | null>(null)
  const qc = useQueryClient()
  // Touch screens use the on-screen keypad: keep the OS keyboard closed. Fine pointers keep autofocus + typing.
  const coarse = useMediaQuery('(pointer: coarse)', false)

  const amount = Number(tendered)
  const valid = tendered !== '' && !Number.isNaN(amount) && amount >= total
  const change = valid ? round2(amount - total) : 0

  const onTenderedChange = (v: string) => {
    if (/^\d{0,7}(\.\d{0,2})?$/.test(v)) setTendered(v)
  }
  const onKey = (k: KeypadKey) =>
    setTendered((t) => {
      if (k === 'Backspace') return t.slice(0, -1)
      if (k === 'Clear') return ''
      if (k === '.') return t.includes('.') ? t : `${t || '0'}.`
      return /^\d{0,7}(\.\d{0,2})?$/.test(t + k) ? (t === '0' ? k : t + k) : t
    })

  const pay = async () => {
    if (!valid || busy) return
    const store = useCartStore.getState()
    const checkout = store.ensureCheckout()
    setBusy(true)
    setError(null)
    try {
      // Idempotent: 201 when created, 200 with the existing order when retried with the same id.
      await orderCalls.create({
        id: checkout.orderId,
        items: lines.map((l) => ({
          itemVariationId: l.variationId,
          quantity: l.quantity,
          modifierIds: l.modifiers.map((m) => m.id),
          note: null,
        })),
      })
      store.markCreated()
      const order = await orderCalls.complete({
        orderId: checkout.orderId,
        idempotencyKey: checkout.idempotencyKey,
        body: { paymentMethod: 'cash', amountTendered: amount },
      })
      void qc.invalidateQueries({ queryKey: ['orders'] })
      void qc.invalidateQueries({ queryKey: ['ingredients'] })
      setDone(order)
      store.finish()
    } catch (e) {
      setError(errorMessage(e))
      // Network failure / 5xx: outcome unknown, keep the same key so a retry cannot double charge.
      // Definitive 4xx: the attempt is over, so the next attempt uses a fresh key.
      if (e instanceof ApiError && e.status < 500) {
        store.newAttemptKey()
        // 409 insufficient stock leaves the order Open server-side; the same order id is reused on retry.
      }
    } finally {
      setBusy(false)
    }
  }

  if (done) {
    const p = done.payments[0]
    return (
      <Modal
        title={`Order #${String(done.orderNumber ?? '').padStart(5, '0')} paid`}
        onClose={onClose}
        variant="full"
        wide
        className="modal-paid"
      >
        <div className="paid-layout">
          <div className="paid-main">
            <div className="paid-banner" role="status">
              <span>Change due</span>
              <strong>{formatMoney(p?.changeGiven ?? 0)}</strong>
              <small>
                Total {formatMoney(done.total)} · Tendered {formatMoney(p?.amountTendered ?? 0)}
              </small>
            </div>
            <div className="paid-actions no-print">
              <button className="btn btn-primary btn-lg btn-block" onClick={onClose}>
                New order
              </button>
              <button className="btn btn-lg btn-block" onClick={() => window.print()}>
                Print again
              </button>
            </div>
          </div>
          <div className="paid-receipt">
            <PrintableReceipt orderId={done.id} autoPrint />
          </div>
        </div>
      </Modal>
    )
  }

  return (
    <Modal
      title="Cash payment"
      onClose={onClose}
      variant="full"
      wide
      className="modal-pay"
      footer={
        <button className="btn btn-primary btn-grow btn-lg" disabled={!valid || busy} onClick={() => void pay()}>
          {busy ? 'Processing…' : `Complete payment · ${formatMoney(total)}`}
        </button>
      }
    >
      <div className="pay-layout">
        <div className="pay-left">
          <dl className="pay-block">
            <div>
              <dt>Total due (VAT incl.)</dt>
              <dd className="big">{formatMoney(total)}</dd>
            </div>
          </dl>
          <label className="field">
            <span className="field-label">Cash tendered (₱)</span>
            <input
              className="input-lg pay-input"
              type="text"
              inputMode={coarse ? 'none' : 'decimal'}
              autoComplete="off"
              autoFocus={!coarse}
              value={tendered}
              onChange={(e) => onTenderedChange(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && void pay()}
            />
          </label>
          <dl className="pay-block">
            <div>
              <dt>Change</dt>
              <dd className={`big${valid ? ' ok' : ''}`}>{valid ? formatMoney(change) : '—'}</dd>
            </div>
          </dl>
          {tendered !== '' && !valid && (
            <p className="field-error" role="alert">
              Tendered amount is less than the total.
            </p>
          )}
          {error && (
            <div className="alert alert-error" role="alert">
              {error}
            </div>
          )}
        </div>
        <div className="pay-right">
          <div className="quick-amounts">
            {quickAmounts(total).map((a) => (
              <button key={a} type="button" className="btn btn-lg" onClick={() => setTendered(String(a))}>
                {a === total ? 'Exact' : tenderLabel(a)}
              </button>
            ))}
          </div>
          <Keypad label="Cash keypad" onKey={onKey} extraKey="." />
        </div>
      </div>
    </Modal>
  )
}
