import { formatMoney } from '../../lib/format'
import { cartCount, cartTotal, lineTotal, useCartStore } from './cartStore'

export function CartPanel({ onCharge }: { onCharge: () => void }) {
  const { lines, setQuantity, remove, clear, sheetOpen, setSheetOpen } = useCartStore()
  const total = cartTotal(lines)

  return (
    <aside className={`cart${sheetOpen ? ' cart-open' : ''}`} aria-label="Current order">
      <div className="cart-head">
        <h2>Current order</h2>
        <div className="cart-head-actions">
          {lines.length > 0 && (
            <button className="btn btn-sm btn-ghost" onClick={clear}>
              Clear
            </button>
          )}
          <button
            className="btn btn-sm btn-icon cart-close"
            onClick={() => setSheetOpen(false)}
            aria-label="Close order"
          >
            ✕
          </button>
        </div>
      </div>

      {lines.length === 0 ? (
        <p className="cart-empty">Tap an item to start an order.</p>
      ) : (
        <ul className="cart-lines">
          {lines.map((l) => (
            <li key={l.key} className="cart-line">
              <div className="cart-line-main">
                <strong>{l.itemName}</strong>
                <span className="muted">{l.variationName}</span>
                {l.modifiers.length > 0 && <span className="muted">+ {l.modifiers.map((m) => m.name).join(', ')}</span>}
              </div>
              <div className="cart-line-side">
                <span>{formatMoney(lineTotal(l))}</span>
                <div className="stepper stepper-sm">
                  <button
                    className="btn btn-icon"
                    onClick={() => setQuantity(l.key, l.quantity - 1)}
                    aria-label={`Decrease ${l.itemName}`}
                  >
                    −
                  </button>
                  <output>{l.quantity}</output>
                  <button
                    className="btn btn-icon"
                    onClick={() => setQuantity(l.key, l.quantity + 1)}
                    aria-label={`Increase ${l.itemName}`}
                  >
                    +
                  </button>
                  <button
                    className="btn btn-icon btn-danger-ghost"
                    onClick={() => remove(l.key)}
                    aria-label={`Remove ${l.itemName}`}
                  >
                    🗑
                  </button>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}

      <div className="cart-foot">
        <div className="cart-total">
          <span>Total ({cartCount(lines)} items)</span>
          <strong>{formatMoney(total)}</strong>
        </div>
        <small className="muted">Prices include 12% VAT</small>
        <button className="btn btn-primary btn-lg btn-block" disabled={lines.length === 0} onClick={onCharge}>
          Charge {formatMoney(total)}
        </button>
      </div>
    </aside>
  )
}
