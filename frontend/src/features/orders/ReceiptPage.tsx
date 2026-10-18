import { Link, useParams } from 'react-router'
import { PageHeader } from '../../components/ui'
import { PrintableReceipt } from './ReceiptView'

export function ReceiptPage() {
  const { id } = useParams()
  return (
    <section className="page narrow">
      <PageHeader title="Receipt">
        <Link className="btn" to="/orders">
          ‹ Orders
        </Link>
        <button className="btn btn-primary" onClick={() => window.print()}>
          Print receipt
        </button>
      </PageHeader>
      {id && <PrintableReceipt orderId={id} />}
    </section>
  )
}
