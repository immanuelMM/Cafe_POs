import { useEffect, useRef, useState } from 'react'
import { IconMinus, IconPlus, IconTrash, IconX } from '../../components/icons'
import { formatMoney } from '../../lib/format'
import { cartCount, cartTotal, lineTotal, useCartStore } from './cartStore'

/** Clearing a cart with more lines than this asks "Clear all?" first. */
const CONFIRM_CLEAR_OVER = 3

export function CartPanel({ onCharge }: { onCharge: () => void }) {
  const { lines, setQuantity, remove, clear, sheetOpen, setSheetOpen } = useCartStore()
  const total = cartTotal(lines)
  const [asking, setAsking] = useState(false)
  const confirming = asking && lines.length > CONFIRM_CLEAR_OVER
  const keepRef = useRef<HTMLButtonElement>(null)

  // The Clear button is replaced by the question; put focus on the safe answer so keyboard users are not dropped.
  useEffect(() => {
    if (confirming) keepRef.current?.focus()
  }, [confirming])

  const onClear = () => {
    if (lines.length > CONFIRM_CLEAR_OVER) setAsking(true)
    else clear()
  }

  return (
    <aside className={`cart${sheetOpen ? ' cart-open' : ''}`} aria-label="Current order">
      <div className="cart-head">
        <h2>Current order</h2>
        <div className="cart-head-actions">
          {lines.length > 0 && (
            <button
              type="button"
              className="btn btn-sm btn-ghost"
              aria-expanded={confirming}
              onClick={confirming ? () => setAsking(false) : onClear}
            >
              Clear
            </button>
          )}
          <button
            type="button"
            className="btn btn-sm btn-icon btn-ghost cart-close"
            onClick={() => setSheetOpen(false)}
            aria-label="Close order"
          >
            <IconX />
          </button>
        </div>
      </div>

      {confirming && (
        <div className="cart-confirm" role="group" aria-label="Confirm clearing the order">
          <span>Clear all {lines.length} lines?</span>
          <div className="cart-confirm-actions">
            <button
              type="button"
              className="btn btn-sm btn-danger"
              onClick={() => {
                setAsking(false)
                clear()
              }}
            >
              Yes, clear
            </button>
            <button type="button" className="btn btn-sm" ref={keepRef} onClick={() => setAsking(false)}>
              Keep
            </button>
          </div>
        </div>
      )}

      {lines.length === 0 ? (
        <p className="cart-empty">Tap an item to start an order.</p>
      ) : (
        <ul className="cart-lines">
          {lines.map((l) => (
            <li key={l.key} className="cart-line">
              <strong className="cart-line-name">{l.itemName}</strong>
              <div className="cart-line-detail muted">
                <span>{l.variationName}</span>
                {l.modifiers.length > 0 && <span>+ {l.modifiers.map((m) => m.name).join(', ')}</span>}
              </div>
              <span className="cart-line-total">{formatMoney(lineTotal(l))}</span>
              <div className="stepper cart-line-qty">
                <button
                  type="button"
                  className="btn btn-icon cart-step"
                  onClick={() => setQuantity(l.key, l.quantity - 1)}
                  aria-label={`Decrease ${l.itemName}`}
                >
                  <IconMinus />
                </button>
                <output>{l.quantity}</output>
                <button
                  type="button"
                  className="btn btn-icon cart-step"
                  onClick={() => setQuantity(l.key, l.quantity + 1)}
                  aria-label={`Increase ${l.itemName}`}
                >
                  <IconPlus />
                </button>
                <button
                  type="button"
                  className="btn btn-icon cart-step btn-danger-ghost"
                  onClick={() => remove(l.key)}
                  aria-label={`Remove ${l.itemName}`}
                >
                  <IconTrash />
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}

      <div className="cart-foot">
        <div className="cart-summary">
          <div className="cart-total">
            <span>Total ({cartCount(lines)} items)</span>
            <strong>{formatMoney(total)}</strong>
          </div>
          <small className="muted">Prices include 12% VAT</small>
        </div>
        <button
          type="button"
          className="btn btn-primary btn-lg btn-block"
          disabled={lines.length === 0}
          onClick={onCharge}
        >
          Charge {formatMoney(total)}
        </button>
      </div>
    </aside>
  )
}
