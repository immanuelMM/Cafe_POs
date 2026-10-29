import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Modal } from '../../components/Modal'
import { ErrorBox, Field, PageHeader, Spinner } from '../../components/ui'
import type { LocationDto } from '../../api/types'
import { client, unwrap } from '../../lib/apiClient'
import { errorMessage } from '../../lib/errors'
import { StaffList } from './StaffList'
import { useLocations } from './useLocations'
import { locationSchema, type LocationValues } from './locationSchema'

function LocationFormModal({ loc, onClose }: { loc: LocationDto | null; onClose: () => void }) {
  const qc = useQueryClient()
  const save = useMutation({
    mutationFn: (v: LocationValues) => {
      const base = {
        name: v.name,
        address: v.address || null,
        timeZone: v.timeZone,
        contactPhone: v.contactPhone || null,
        vatRatePercent: v.vatRatePercent,
      }
      // Contract: on update an empty string clears the Stripe id (omitted = unchanged).
      return loc
        ? unwrap(
            client.PUT('/api/v1/locations/{id}', {
              params: { path: { id: loc.id } },
              body: { ...base, active: v.active, stripeTerminalLocationId: v.stripeTerminalLocationId ?? '' },
            }),
          )
        : unwrap(
            client.POST('/api/v1/locations', {
              body: { ...base, stripeTerminalLocationId: v.stripeTerminalLocationId || null },
            }),
          )
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['locations'] })
      void qc.invalidateQueries({ queryKey: ['location'] })
      onClose()
    },
  })
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<LocationValues>({
    resolver: zodResolver(locationSchema),
    defaultValues: {
      name: loc?.name ?? '',
      address: loc?.address ?? '',
      timeZone: loc?.timeZone ?? 'Asia/Manila',
      contactPhone: loc?.contactPhone ?? '',
      vatRatePercent: loc?.vatRatePercent ?? 12,
      stripeTerminalLocationId: loc?.stripeTerminalLocationId ?? '',
      active: loc?.active ?? true,
    },
  })
  return (
    <Modal title={loc ? `Edit ${loc.name}` : 'New location'} onClose={onClose}>
      <form className="form" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
        {save.error && (
          <div className="alert alert-error" role="alert">
            {errorMessage(save.error)}
          </div>
        )}
        <Field label="Name" error={errors.name?.message}>
          <input {...register('name')} />
        </Field>
        <Field label="Address (printed on receipts)">
          <input {...register('address')} />
        </Field>
        <div className="grid-2">
          <Field label="Phone">
            <input type="tel" {...register('contactPhone')} />
          </Field>
          <Field label="Time zone (IANA)" error={errors.timeZone?.message}>
            <input {...register('timeZone')} />
          </Field>
        </div>
        <Field label="VAT rate (%)" error={errors.vatRatePercent?.message}>
          <input
            type="number"
            step="0.01"
            inputMode="decimal"
            {...register('vatRatePercent', { valueAsNumber: true })}
          />
        </Field>
        <Field label="Stripe Terminal location id (optional)">
          <input {...register('stripeTerminalLocationId')} />
        </Field>
        {loc && (
          <label className="check">
            <input type="checkbox" {...register('active')} /> Active
          </label>
        )}
        <button className="btn btn-primary" disabled={save.isPending}>
          {save.isPending ? 'Saving…' : loc ? 'Save changes' : 'Create location'}
        </button>
      </form>
    </Modal>
  )
}

type Dialog = { kind: 'new' } | { kind: 'edit' | 'staff'; loc: LocationDto } | null

/** Owner only: every location, create/edit, and the per-location staff roster. */
export function LocationsPage() {
  const q = useLocations()
  const [dialog, setDialog] = useState<Dialog>(null)
  const close = () => setDialog(null)
  return (
    <section className="page">
      <PageHeader title="Locations">
        <button className="btn btn-primary" onClick={() => setDialog({ kind: 'new' })}>
          + New location
        </button>
      </PageHeader>
      {q.isLoading && <Spinner />}
      {q.error && <ErrorBox error={q.error} onRetry={() => void q.refetch()} />}
      <ul className="row-list">
        {q.data?.map((l) => (
          <li key={l.id} className="row-item">
            <div className="row-main">
              <strong>{l.name}</strong>
              <small className="muted">
                {[l.address, l.contactPhone, l.timeZone, `VAT ${l.vatRatePercent}%`].filter(Boolean).join(' · ')}
              </small>
            </div>
            {!l.active && <span className="badge badge-canceled">Inactive</span>}
            <div className="row-actions">
              <button className="btn btn-sm" onClick={() => setDialog({ kind: 'staff', loc: l })}>
                Staff
              </button>
              <button className="btn btn-sm btn-primary" onClick={() => setDialog({ kind: 'edit', loc: l })}>
                Edit
              </button>
            </div>
          </li>
        ))}
      </ul>
      {dialog?.kind === 'new' && <LocationFormModal loc={null} onClose={close} />}
      {dialog?.kind === 'edit' && <LocationFormModal loc={dialog.loc} onClose={close} />}
      {dialog?.kind === 'staff' && (
        <Modal title={`Staff: ${dialog.loc.name}`} onClose={close}>
          <StaffList locationId={dialog.loc.id} />
        </Modal>
      )}
    </section>
  )
}
