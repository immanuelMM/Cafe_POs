import { useState } from 'react'
import { Modal } from '../../components/Modal'
import { ErrorBox, Spinner } from '../../components/ui'
import { errorMessage } from '../../lib/api'
import { formatMoney } from '../../lib/format'
import type { LocationOverride, Variation } from '../../lib/types'
import { useAuthStore } from '../auth/authStore'
import { useLocations } from '../locations/locationsApi'
import { saveOverride, useCatalogMutation, useLocationOverrides } from './catalogApi'

const numOrNull = (s: string) => (s.trim() === '' ? null : Number(s))

function OverrideRow({ variation, locationId, locationName, current }: { variation: Variation; locationId: string; locationName: string; current?: LocationOverride }) {
  const [price, setPrice] = useState(current?.priceOverride?.toString() ?? '')
  const [track, setTrack] = useState(current?.trackInventoryOverride == null ? '' : String(current.trackInventoryOverride))
  const [threshold, setThreshold] = useState(current?.lowStockThresholdOverride?.toString() ?? '')
  const save = useCatalogMutation(saveOverride, ['location-overrides'])

  const priceN = numOrNull(price)
  const thrN = numOrNull(threshold)
  const invalid = (priceN !== null && (Number.isNaN(priceN) || priceN < 0)) || (thrN !== null && (Number.isNaN(thrN) || thrN < 0))
  const body = { priceOverride: priceN, trackInventoryOverride: track === '' ? null : track === 'true', lowStockThresholdOverride: thrN }
  const isEmpty = body.priceOverride === null && body.trackInventoryOverride === null && body.lowStockThresholdOverride === null

  const clear = () => {
    setPrice('')
    setTrack('')
    setThreshold('')
    save.mutate({ variationId: variation.id, locationId, body: { priceOverride: null, trackInventoryOverride: null, lowStockThresholdOverride: null } })
  }

  return (
    <fieldset className="override-row">
      <legend>{locationName}</legend>
      <label className="field">
        <span className="field-label">Price (₱, VAT incl.)</span>
        <input type="number" step="0.01" min="0" inputMode="decimal" placeholder={`Default ${formatMoney(variation.price)}`} value={price} onChange={(e) => setPrice(e.target.value)} />
      </label>
      <label className="field">
        <span className="field-label">Track inventory</span>
        <select value={track} onChange={(e) => setTrack(e.target.value)}>
          <option value="">Default (tracked)</option>
          <option value="true">Tracked</option>
          <option value="false">Do not track</option>
        </select>
      </label>
      <label className="field">
        <span className="field-label">Low stock threshold</span>
        <input type="number" step="0.001" min="0" inputMode="decimal" placeholder="Default" value={threshold} onChange={(e) => setThreshold(e.target.value)} />
        <span className="field-hint">Stored only, not enforced yet.</span>
      </label>
      <div className="var-actions">
        <button className="btn btn-sm btn-primary" disabled={invalid || save.isPending || (isEmpty && !current)} onClick={() => save.mutate({ variationId: variation.id, locationId, body })}>
          {isEmpty && current ? 'Remove override' : 'Save'}
        </button>
        {current && !isEmpty && (
          <button className="btn btn-sm btn-danger-ghost" disabled={save.isPending} onClick={clear}>
            Remove override
          </button>
        )}
        {save.isSuccess && !save.isPending && (
          <span className="muted" role="status">
            Saved.
          </span>
        )}
      </div>
      {save.error && <div className="alert alert-error" role="alert">{errorMessage(save.error)}</div>}
    </fieldset>
  )
}

/** Per-location price / inventory-tracking / low-stock overrides for one variation. */
export function OverridesEditor({ variation, onClose }: { variation: Variation; onClose: () => void }) {
  const user = useAuthStore((s) => s.user)
  const owner = !!user?.isOwner
  const q = useLocationOverrides(variation.id)
  const locs = useLocations(owner)

  // Owner: every active location (the API only lists existing overrides). Others: their active location.
  const rows = owner
    ? (locs.data ?? []).filter((l) => l.active).map((l) => ({ id: l.id, name: l.name }))
    : user?.location
      ? [user.location]
      : []
  const loading = q.isLoading || (owner && locs.isLoading)

  return (
    <Modal title={`Location prices: ${variation.name}`} onClose={onClose} wide>
      <p className="muted">Global price {formatMoney(variation.price)}. Leave a field empty to use the default. Saving with everything empty removes the override.</p>
      {loading && <Spinner />}
      {q.error && <ErrorBox error={q.error} onRetry={() => void q.refetch()} />}
      {!loading && rows.length === 0 && <p className="muted">No location to configure. Pick a location in the header first.</p>}
      {!loading &&
        !q.error &&
        rows.map((l) => {
          const cur = q.data?.find((o) => o.locationId === l.id)
          return (
            <OverrideRow
              key={`${l.id}-${cur?.priceOverride}-${cur?.trackInventoryOverride}-${cur?.lowStockThresholdOverride}`}
              variation={variation}
              locationId={l.id}
              locationName={l.name}
              current={cur}
            />
          )
        })}
    </Modal>
  )
}
