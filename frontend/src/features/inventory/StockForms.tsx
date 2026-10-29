import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Modal } from '../../components/Modal'
import { Field, Pager, Spinner, ErrorBox } from '../../components/ui'
import { errorMessage } from '../../lib/errors'
import { formatDateTime, formatMoney, formatQty } from '../../lib/format'
import type { Ingredient } from '../../api/types'
import { calls, useMovements } from './useInventory'
import {
  adjSchema,
  ingSchema,
  restockSchema,
  type AdjValues,
  type IngValues,
  type RestockValues,
} from './inventorySchema'

const optNum = (v: string) => (v === '' ? undefined : Number(v))

function useInvMutation<T, R>(fn: (v: T) => Promise<R>, onDone: () => void) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['ingredients'] })
      void qc.invalidateQueries({ queryKey: ['movements'] })
      onDone()
    },
  })
}

export function IngredientForm({ ing, onClose }: { ing: Ingredient | null; onClose: () => void }) {
  const save = useInvMutation(calls.saveIngredient, onClose)
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<IngValues>({
    resolver: zodResolver(ingSchema),
    defaultValues: {
      name: ing?.name ?? '',
      unit: ing?.unit ?? '',
      lowStockThreshold: ing?.lowStockThreshold ?? 0,
      initialQuantity: 0,
      active: ing?.active ?? true,
    },
  })
  return (
    <Modal title={ing ? `Edit ${ing.name}` : 'New ingredient'} onClose={onClose}>
      <form
        className="form"
        onSubmit={handleSubmit((v) =>
          save.mutate({
            id: ing?.id,
            body: ing
              ? { name: v.name, unit: v.unit, lowStockThreshold: v.lowStockThreshold, active: v.active }
              : {
                  name: v.name,
                  unit: v.unit,
                  lowStockThreshold: v.lowStockThreshold,
                  initialQuantity: v.initialQuantity,
                },
          }),
        )}
      >
        {save.error && (
          <div className="alert alert-error" role="alert">
            {errorMessage(save.error)}
          </div>
        )}
        <Field label="Name" error={errors.name?.message}>
          <input {...register('name')} />
        </Field>
        <Field label="Unit" error={errors.unit?.message}>
          <input placeholder="ml, g, each" {...register('unit')} />
        </Field>
        <Field
          label="Low-stock threshold"
          error={errors.lowStockThreshold?.message}
          hint="Flagged when on-hand is at or below this"
        >
          <input
            type="number"
            step="0.001"
            inputMode="decimal"
            {...register('lowStockThreshold', { valueAsNumber: true })}
          />
        </Field>
        {ing ? (
          <label className="check">
            <input type="checkbox" {...register('active')} /> Active
          </label>
        ) : (
          <Field label="Opening quantity" error={errors.initialQuantity?.message}>
            <input
              type="number"
              step="0.001"
              inputMode="decimal"
              {...register('initialQuantity', { valueAsNumber: true })}
            />
          </Field>
        )}
        <button className="btn btn-primary btn-block" disabled={save.isPending}>
          {save.isPending ? 'Saving…' : 'Save'}
        </button>
      </form>
    </Modal>
  )
}

export function RestockForm({ ing, onClose }: { ing: Ingredient; onClose: () => void }) {
  const save = useInvMutation(calls.restock, onClose)
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<RestockValues>({ resolver: zodResolver(restockSchema) })
  return (
    <Modal title={`Restock ${ing.name}`} onClose={onClose}>
      <form
        className="form"
        onSubmit={handleSubmit((v) =>
          save.mutate({
            ingredientId: ing.id,
            quantity: v.quantity,
            unitCost: v.unitCost,
            supplier: v.supplier || undefined,
            note: v.note || undefined,
          }),
        )}
      >
        <p className="muted">
          On hand: {formatQty(ing.quantityOnHand ?? 0)} {ing.unit}
        </p>
        {save.error && (
          <div className="alert alert-error" role="alert">
            {errorMessage(save.error)}
          </div>
        )}
        <Field label={`Quantity received (${ing.unit})`} error={errors.quantity?.message}>
          <input
            type="number"
            step="0.001"
            inputMode="decimal"
            autoFocus
            {...register('quantity', { valueAsNumber: true })}
          />
        </Field>
        <Field label="Unit cost (₱, optional)" error={errors.unitCost?.message}>
          <input type="number" step="0.01" inputMode="decimal" {...register('unitCost', { setValueAs: optNum })} />
        </Field>
        <Field label="Supplier (optional)">
          <input {...register('supplier')} />
        </Field>
        <Field label="Note (optional)">
          <input {...register('note')} />
        </Field>
        <button className="btn btn-primary btn-block" disabled={save.isPending}>
          {save.isPending ? 'Saving…' : 'Record restock'}
        </button>
      </form>
    </Modal>
  )
}

export function AdjustForm({ ing, onClose }: { ing: Ingredient; onClose: () => void }) {
  const save = useInvMutation(calls.adjust, onClose)
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<AdjValues>({ resolver: zodResolver(adjSchema), defaultValues: { kind: 'Waste' } })
  return (
    <Modal title={`Adjust / waste: ${ing.name}`} onClose={onClose}>
      <form
        className="form"
        onSubmit={handleSubmit((v) =>
          save.mutate({
            id: ing.id,
            reason: v.kind === 'Waste' ? 'Waste' : 'Adjustment',
            quantityDelta: v.kind === 'Adjustment+' ? v.quantity : -v.quantity,
            note: v.note,
          }),
        )}
      >
        <p className="muted">
          On hand: {formatQty(ing.quantityOnHand ?? 0)} {ing.unit}
        </p>
        {save.error && (
          <div className="alert alert-error" role="alert">
            {errorMessage(save.error)}
          </div>
        )}
        <Field label="Type">
          <select {...register('kind')}>
            <option value="Waste">Waste / spoilage (removes stock)</option>
            <option value="Adjustment-">Count correction: remove stock</option>
            <option value="Adjustment+">Count correction: add stock</option>
          </select>
        </Field>
        <Field label={`Quantity (${ing.unit})`} error={errors.quantity?.message}>
          <input type="number" step="0.001" inputMode="decimal" {...register('quantity', { valueAsNumber: true })} />
        </Field>
        <Field label="Note" error={errors.note?.message}>
          <input {...register('note')} />
        </Field>
        <button className="btn btn-primary btn-block" disabled={save.isPending}>
          {save.isPending ? 'Saving…' : 'Save'}
        </button>
      </form>
    </Modal>
  )
}

export function MovementsModal({
  ing,
  locationId,
  locationName,
  onClose,
}: {
  ing: Ingredient
  locationId?: string
  locationName?: string
  onClose: () => void
}) {
  const [page, setPage] = useState(1)
  const q = useMovements(ing.id, page, locationId)
  return (
    <Modal title={`Stock ledger: ${ing.name}${locationName ? ` (${locationName})` : ''}`} onClose={onClose} wide>
      {q.isLoading && <Spinner />}
      {q.error && <ErrorBox error={q.error} onRetry={() => void q.refetch()} />}
      {q.data && q.data.data.length === 0 && <p className="muted">No movements yet.</p>}
      <ul className="ledger">
        {q.data?.data.map((m) => (
          <li key={m.id} className="ledger-row">
            <span className={`delta ${m.quantityDelta < 0 ? 'neg' : 'pos'}`}>
              {m.quantityDelta > 0 ? '+' : ''}
              {formatQty(m.quantityDelta)} {ing.unit}
            </span>
            <span className={`badge badge-reason-${m.reason.toLowerCase()}`}>{m.reason}</span>
            <span className="muted grow">
              {[
                m.note,
                m.supplier && `from ${m.supplier}`,
                m.unitCost != null && `@ ${formatMoney(m.unitCost)}`,
                m.sourceDocumentRef,
              ]
                .filter(Boolean)
                .join(' · ')}
            </span>
            <time className="muted" dateTime={m.createdAt}>
              {formatDateTime(m.createdAt)}
            </time>
          </li>
        ))}
      </ul>
      {q.data && <Pager page={q.data.page} totalPages={q.data.totalPages} onPage={setPage} />}
    </Modal>
  )
}
