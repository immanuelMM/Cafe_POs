import { Link, useParams } from 'react-router'
import { PrintableReceipt } from './ReceiptView'

/** Large on-screen receipt with a sticky action bar. Print CSS (80mm / 58mm) is unchanged. */
export function ReceiptPage() {
  const { id } = useParams()
  return (
    <section className="receipt-page" aria-label="Receipt">
      <div className="receipt-page-scroll">
        <h1 className="receipt-page-title">Receipt</h1>
        {id && <PrintableReceipt orderId={id} />}
      </div>
      <div className="receipt-actions no-print">
        <Link className="btn btn-lg" to="/orders">
          ‹ Back to orders
        </Link>
        <button className="btn btn-primary btn-lg" onClick={() => window.print()}>
          Print
        </button>
      </div>
    </section>
  )
}
