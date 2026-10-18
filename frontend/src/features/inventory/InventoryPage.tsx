import { useMemo, useState } from 'react'
import { ErrorBox, PageHeader, Spinner } from '../../components/ui'
import { formatQty } from '../../lib/format'
import type { Ingredient } from '../../lib/types'
import { STATUS_LABEL, stockStatus, useIngredients } from './inventoryApi'
import { AdjustForm, IngredientForm, MovementsModal, RestockForm } from './StockForms'

type Dialog = { kind: 'new' } | { kind: 'edit' | 'restock' | 'adjust' | 'history'; ing: Ingredient } | null

export function InventoryPage() {
  const q = useIngredients()
  const [lowOnly, setLowOnly] = useState(false)
  const [search, setSearch] = useState('')
  const [dialog, setDialog] = useState<Dialog>(null)
  const close = () => setDialog(null)

  const rows = useMemo(() => {
    const all = (q.data ?? []).filter((i) => i.active)
    return all
      .filter((i) => (!lowOnly || i.isLowStock) && i.name.toLowerCase().includes(search.trim().toLowerCase()))
      .sort((a, b) => Number(b.isLowStock) - Number(a.isLowStock) || a.name.localeCompare(b.name))
  }, [q.data, lowOnly, search])

  const lowCount = (q.data ?? []).filter((i) => i.active && i.isLowStock).length

  return (
    <section className="page">
      <PageHeader title="Inventory">
        <button className="btn btn-primary" onClick={() => setDialog({ kind: 'new' })}>
          + New ingredient
        </button>
      </PageHeader>

      <div className="summary">
        <div className={`stat ${lowCount > 0 ? 'stat-low' : 'stat-ok'}`}>
          <strong>{lowCount}</strong>
          <span>low or out of stock</span>
        </div>
        <div className="stat">
          <strong>{(q.data ?? []).filter((i) => i.active).length}</strong>
          <span>active ingredients</span>
        </div>
      </div>

      <div className="toolbar">
        <input type="search" placeholder="Search ingredients…" aria-label="Search ingredients" value={search} onChange={(e) => setSearch(e.target.value)} />
        <label className="check">
          <input type="checkbox" checked={lowOnly} onChange={(e) => setLowOnly(e.target.checked)} /> Low stock only
        </label>
      </div>

      {q.isLoading && <Spinner label="Loading stock…" />}
      {q.error && <ErrorBox error={q.error} onRetry={() => void q.refetch()} />}
      {q.data && rows.length === 0 && <p className="muted">Nothing to show.</p>}

      <ul className="stock-list">
        {rows.map((i) => {
          const st = stockStatus(i)
          return (
            <li key={i.id} className={`stock-row stock-${st}`}>
              <div className="stock-name">
                <strong>{i.name}</strong>
                <span className={`pill pill-${st}`}>{STATUS_LABEL[st]}</span>
              </div>
              <div className="stock-qty">
                <strong>
                  {formatQty(i.quantityOnHand)} {i.unit}
                </strong>
                <small className="muted">
                  threshold {formatQty(i.lowStockThreshold)} {i.unit}
                </small>
              </div>
              <div className="stock-actions">
                <button className="btn btn-sm btn-primary" onClick={() => setDialog({ kind: 'restock', ing: i })}>
                  Restock
                </button>
                <button className="btn btn-sm" onClick={() => setDialog({ kind: 'adjust', ing: i })}>
                  Adjust / waste
                </button>
                <button className="btn btn-sm" onClick={() => setDialog({ kind: 'history', ing: i })}>
                  Ledger
                </button>
                <button className="btn btn-sm btn-ghost" onClick={() => setDialog({ kind: 'edit', ing: i })}>
                  Edit
                </button>
              </div>
            </li>
          )
        })}
      </ul>

      {dialog?.kind === 'new' && <IngredientForm ing={null} onClose={close} />}
      {dialog?.kind === 'edit' && <IngredientForm ing={dialog.ing} onClose={close} />}
      {dialog?.kind === 'restock' && <RestockForm ing={dialog.ing} onClose={close} />}
      {dialog?.kind === 'adjust' && <AdjustForm ing={dialog.ing} onClose={close} />}
      {dialog?.kind === 'history' && <MovementsModal ing={dialog.ing} onClose={close} />}
    </section>
  )
}
