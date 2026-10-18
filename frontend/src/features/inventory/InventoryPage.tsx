import { useMemo, useState } from 'react'
import { ErrorBox, PageHeader, Spinner } from '../../components/ui'
import { useAuthStore } from '../auth/authStore'
import { useLocations } from '../locations/locationsApi'
import { formatQty } from '../../lib/format'
import type { Ingredient } from '../../lib/types'
import { STATUS_LABEL, stockStatus, useIngredients } from './inventoryApi'
import { AdjustForm, IngredientForm, MovementsModal, RestockForm } from './StockForms'

type Dialog = { kind: 'new' } | { kind: 'edit' | 'restock' | 'adjust' | 'history'; ing: Ingredient } | null

export function InventoryPage() {
  const user = useAuthStore((s) => s.user)
  const owner = !!user?.isOwner
  const [locationId, setLocationId] = useState('')
  const locations = useLocations(owner)
  // Owner without an active location and without a filter gets the global catalog (no stock figures).
  const catalogOnly = owner && !user?.location && !locationId
  const q = useIngredients(owner ? locationId || undefined : undefined)
  const viewedId = (owner && locationId) || user?.location?.id
  const viewedName = locations.data?.find((l) => l.id === viewedId)?.name ?? user?.location?.name
  // Restocks, adjustments and new stock always post to the ACTIVE location, so only allow them when viewing it.
  const canWrite = !!user?.location && (!locationId || locationId === user.location.id)
  const [lowOnly, setLowOnly] = useState(false)
  const [search, setSearch] = useState('')
  const [dialog, setDialog] = useState<Dialog>(null)
  const close = () => setDialog(null)

  const rows = useMemo(() => {
    const all = (q.data ?? []).filter((i) => i.active)
    return all
      .filter((i) => (!lowOnly || i.isLowStock === true) && i.name.toLowerCase().includes(search.trim().toLowerCase()))
      .sort((a, b) => Number(b.isLowStock === true) - Number(a.isLowStock === true) || a.name.localeCompare(b.name))
  }, [q.data, lowOnly, search])

  const lowCount = (q.data ?? []).filter((i) => i.active && i.isLowStock === true).length

  return (
    <section className="page">
      <PageHeader title="Inventory">
        <button className="btn btn-primary" disabled={!canWrite} onClick={() => setDialog({ kind: 'new' })}>
          + New ingredient
        </button>
      </PageHeader>

      {owner && (
        <div className="toolbar">
          <label className="field loc-filter">
            <span className="field-label">Location</span>
            <select value={locationId} onChange={(e) => setLocationId(e.target.value)}>
              <option value="">{user?.location ? `Current (${user.location.name})` : 'All locations (catalog only)'}</option>
              {(locations.data ?? []).map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name}
                  {l.active ? '' : ' (inactive)'}
                </option>
              ))}
            </select>
          </label>
        </div>
      )}
      {catalogOnly && <p className="muted">Showing the ingredient catalog only. Pick a location above to see its stock levels.</p>}
      {!catalogOnly && !canWrite && (
        <p className="muted">
          Viewing {viewedName ?? 'another location'} read-only. Switch your active location in the header to restock or adjust stock there.
        </p>
      )}

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
                {i.quantityOnHand !== null && <span className={`pill pill-${st}`}>{STATUS_LABEL[st]}</span>}
              </div>
              <div className="stock-qty">
                <strong>
                  {i.quantityOnHand === null ? '�' : formatQty(i.quantityOnHand)} {i.unit}
                </strong>
                <small className="muted">
                  threshold {formatQty(i.lowStockThreshold)} {i.unit}
                </small>
              </div>
              <div className="stock-actions">
                <button className="btn btn-sm btn-primary" disabled={!canWrite} onClick={() => setDialog({ kind: 'restock', ing: i })}>
                  Restock
                </button>
                <button className="btn btn-sm" disabled={!canWrite} onClick={() => setDialog({ kind: 'adjust', ing: i })}>
                  Adjust / waste
                </button>
                <button className="btn btn-sm" disabled={catalogOnly} onClick={() => setDialog({ kind: 'history', ing: i })}>
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
      {dialog?.kind === 'history' && <MovementsModal ing={dialog.ing} locationId={viewedId} locationName={viewedName} onClose={close} />}
    </section>
  )
}
