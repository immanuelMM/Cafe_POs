import { useEffect } from 'react'
import { createPortal } from 'react-dom'
import { formatDateTime, formatMoney, formatQty } from '../../lib/format'
import type { Receipt } from '../../api/types'
import { ErrorBox, Spinner } from '../../components/ui'
import { useReceipt } from './useReceipt'
import './orders.css'

/** The 80mm receipt layout. Prices are VAT-inclusive: VAT is shown as a portion of the total, never added. */
export function ReceiptView({ r }: { r: Receipt }) {
  const cash = r.paymentMethod.toLowerCase() === 'cash'
  return (
    <article className="receipt" aria-label={`Receipt ${r.orderNumber}`}>
      <header className="receipt-head">
        <strong className="receipt-name">{r.locationName}</strong>
        {r.locationAddress && <div>{r.locationAddress}</div>}
        {r.locationPhone && <div>Tel {r.locationPhone}</div>}
      </header>
      <hr />
      <div className="receipt-meta">
        <div>
          <span>Order #</span>
          <strong>{String(r.orderNumber).padStart(5, '0')}</strong>
        </div>
        <div>
          <span>Date</span>
          <span>{formatDateTime(r.completedAt, r.timeZone)}</span>
        </div>
        <div>
          <span>Cashier</span>
          <span>{r.cashierName}</span>
        </div>
      </div>
      <hr />
      <ul className="receipt-lines">
        {r.lines.map((l, i) => (
          <li key={i}>
            <div className="receipt-line">
              <span>
                {formatQty(l.quantity)} × {l.description}
              </span>
              <span>{formatMoney(l.lineTotal)}</span>
            </div>
            {l.modifiers && <div className="receipt-mods">+ {l.modifiers}</div>}
            {l.quantity !== 1 && <div className="receipt-mods">@ {formatMoney(l.unitPrice)} each</div>}
          </li>
        ))}
      </ul>
      <hr />
      <dl className="receipt-totals">
        <div className="receipt-total">
          <dt>TOTAL</dt>
          <dd>{formatMoney(r.total)}</dd>
        </div>
        {r.pricesIncludeVat && (
          <>
            <div>
              <dt>VATable sales</dt>
              <dd>{formatMoney(r.vatableSales)}</dd>
            </div>
            <div>
              <dt>VAT ({formatQty(r.vatRatePercent)}%) incl.</dt>
              <dd>{formatMoney(r.vatAmount)}</dd>
            </div>
          </>
        )}
        <div>
          <dt>{cash ? 'Cash' : r.paymentMethod}</dt>
          <dd>{formatMoney(r.amountTendered ?? r.total)}</dd>
        </div>
        <div>
          <dt>Change</dt>
          <dd>{formatMoney(r.changeGiven ?? 0)}</dd>
        </div>
      </dl>
      <hr />
      <footer className="receipt-foot">
        <div>Prices include VAT.</div>
        <div>Thank you! Please come again.</div>
        <div className="receipt-disclaimer">This is not an official BIR receipt.</div>
      </footer>
    </article>
  )
}

/** Orders already auto-printed in this page session: one print per completed order, however often it re-renders or remounts. */
const autoPrinted = new Set<string>()

/**
 * On-screen preview plus a portal copy into #print-root that is the only thing shown by the print stylesheet.
 * `autoPrint` opens the browser print dialog once when the receipt has rendered (the browser cannot print silently).
 */
export function PrintableReceipt({ orderId, autoPrint }: { orderId: string; autoPrint?: boolean }) {
  const { data, isLoading, error, refetch } = useReceipt(orderId)
  const ready = !!data
  useEffect(() => {
    if (!autoPrint || !ready || autoPrinted.has(orderId)) return
    // Marked inside the timer, so a StrictMode effect re-run (cleanup, then run again) still prints exactly once.
    const t = window.setTimeout(() => {
      if (autoPrinted.has(orderId)) return
      autoPrinted.add(orderId)
      window.print()
    }, 250)
    return () => window.clearTimeout(t)
  }, [autoPrint, ready, orderId])
  if (isLoading) return <Spinner label="Loading receipt…" />
  if (error || !data) return <ErrorBox error={error} onRetry={() => void refetch()} />
  const printRoot = document.getElementById('print-root')
  return (
    <>
      <div className="receipt-preview">
        <ReceiptView r={data} />
      </div>
      {printRoot && createPortal(<ReceiptView r={data} />, printRoot)}
    </>
  )
}
