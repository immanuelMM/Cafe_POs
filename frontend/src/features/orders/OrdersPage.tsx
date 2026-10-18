import { useState } from 'react'
import { Link } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ErrorBox, PageHeader, Pager, Spinner } from '../../components/ui'
import { api, errorMessage } from '../../lib/api'
import { formatDateTime, formatMoney } from '../../lib/format'
import type { LocationDto, Order, OrderStatus, Paged } from '../../lib/types'

const PAGE_SIZE = 15

/** Local-day boundaries as ISO timestamps. */
const dayStart = (d: string) => new Date(`${d}T00:00:00`).toISOString()
const dayEnd = (d: string) => new Date(`${d}T23:59:59.999`).toISOString()

export function OrdersPage() {
  const [status, setStatus] = useState<'' | OrderStatus>('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [page, setPage] = useState(1)
  const qc = useQueryClient()

  const loc = useQuery({ queryKey: ['location'], queryFn: () => api<LocationDto>('/locations/current') })
  const tz = loc.data?.timeZone ?? 'Asia/Manila'

  const orders = useQuery({
    queryKey: ['orders', { status, from, to, page }],
    queryFn: () =>
      api<Paged<Order>>('/orders', {
        query: { status, from: from ? dayStart(from) : undefined, to: to ? dayEnd(to) : undefined, page, pageSize: PAGE_SIZE },
      }),
    placeholderData: (prev) => prev,
  })

  const cancel = useMutation({
    mutationFn: (id: string) => api<Order>(`/orders/${id}/cancel`, { method: 'POST' }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['orders'] }),
  })

  const reset = <T,>(set: (v: T) => void) => (v: T) => {
    set(v)
    setPage(1)
  }

  return (
    <section className="page">
      <PageHeader title="Order history" />
      <form className="filters" onSubmit={(e) => e.preventDefault()}>
        <label className="field">
          <span className="field-label">Status</span>
          <select value={status} onChange={(e) => reset(setStatus)(e.target.value as '' | OrderStatus)}>
            <option value="">All</option>
            <option value="Completed">Completed</option>
            <option value="Open">Open</option>
            <option value="Canceled">Canceled</option>
          </select>
        </label>
        <label className="field">
          <span className="field-label">From</span>
          <input type="date" value={from} max={to || undefined} onChange={(e) => reset(setFrom)(e.target.value)} />
        </label>
        <label className="field">
          <span className="field-label">To</span>
          <input type="date" value={to} min={from || undefined} onChange={(e) => reset(setTo)(e.target.value)} />
        </label>
        <button
          type="button"
          className="btn"
          onClick={() => {
            setStatus('')
            setFrom('')
            setTo('')
            setPage(1)
          }}
        >
          Clear filters
        </button>
      </form>

      {orders.isLoading && <Spinner label="Loading orders…" />}
      {orders.error && <ErrorBox error={orders.error} onRetry={() => void orders.refetch()} />}
      {cancel.error && <ErrorBox error={cancel.error} />}
      {orders.data && orders.data.data.length === 0 && <p className="muted">No orders match these filters.</p>}

      <ul className="order-list">
        {orders.data?.data.map((o) => (
          <li key={o.id} className="order-card">
            <details>
              <summary>
                <span className="order-no">{o.orderNumber != null ? `#${String(o.orderNumber).padStart(5, '0')}` : 'Open'}</span>
                <span className="muted order-when">{formatDateTime(o.completedAt ?? o.createdAt, tz)}</span>
                <span className="muted order-cashier">{o.cashierName}</span>
                <span className={`badge badge-${o.status.toLowerCase()}`}>{o.status}</span>
                <strong className="order-total">{formatMoney(o.total)}</strong>
              </summary>
              <div className="order-detail">
                <ul>
                  {o.items.map((l) => (
                    <li key={l.id}>
                      {l.quantity} × {l.itemName} ({l.variationName})
                      {l.modifiers.length > 0 && <span className="muted"> + {l.modifiers.map((m) => m.name).join(', ')}</span>}
                      <span className="grow" />
                      {formatMoney(l.lineTotal)}
                    </li>
                  ))}
                </ul>
                <p className="muted">
                  VAT ({o.vatRatePercent}%) included: {formatMoney(o.vatAmount)}
                  {o.payments[0] && ` · ${o.payments[0].method} tendered ${formatMoney(o.payments[0].amountTendered)}, change ${formatMoney(o.payments[0].changeGiven)}`}
                </p>
                <div className="row-actions">
                  {o.status === 'Completed' && (
                    <Link className="btn btn-primary" to={`/orders/${o.id}/receipt`}>
                      View receipt
                    </Link>
                  )}
                  {o.status === 'Open' && (
                    <button className="btn" disabled={cancel.isPending} onClick={() => cancel.mutate(o.id)}>
                      Cancel order
                    </button>
                  )}
                </div>
                {cancel.isError && cancel.variables === o.id && <p className="field-error">{errorMessage(cancel.error)}</p>}
              </div>
            </details>
          </li>
        ))}
      </ul>
      {orders.data && <Pager page={orders.data.page} totalPages={orders.data.totalPages} onPage={setPage} />}
      {orders.data && <p className="muted center">{orders.data.totalCount} orders</p>}
    </section>
  )
}
