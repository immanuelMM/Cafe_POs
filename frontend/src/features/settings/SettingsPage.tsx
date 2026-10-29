import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ErrorBox, Field, PageHeader, Spinner } from '../../components/ui'
import type { LocationDto } from '../../api/types'
import { client, unwrap } from '../../lib/apiClient'
import { errorMessage } from '../../lib/errors'
import { useAuthStore } from '../auth/authStore'
import { StaffList } from '../locations/StaffList'
import { useCurrentLocation } from '../locations/useLocations'
import { useBranding } from '../branding/useBranding'
import { BrandSettings } from '../branding/BrandSettings'
import { locationSettingsSchema, type LocationSettingsValues } from './settingsSchema'

function LocationForm({ loc }: { loc: LocationDto }) {
  const qc = useQueryClient()
  const [saved, setSaved] = useState(false)
  const save = useMutation({
    mutationFn: (v: LocationSettingsValues) =>
      unwrap(
        client.PUT('/api/v1/locations/{id}', {
          params: { path: { id: loc.id } },
          body: { ...v, address: v.address || null, contactPhone: v.contactPhone || null },
        }),
      ),
    onSuccess: () => {
      setSaved(true)
      void qc.invalidateQueries({ queryKey: ['location'] })
      void qc.invalidateQueries({ queryKey: ['locations'] })
    },
  })
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isDirty },
  } = useForm<LocationSettingsValues>({
    resolver: zodResolver(locationSettingsSchema),
    defaultValues: {
      name: loc.name,
      address: loc.address ?? '',
      timeZone: loc.timeZone,
      contactPhone: loc.contactPhone ?? '',
      vatRatePercent: loc.vatRatePercent,
    },
  })
  return (
    <form
      className="form card"
      onSubmit={handleSubmit((v) => {
        setSaved(false)
        save.mutate(v, { onSuccess: () => reset(v) })
      })}
    >
      {save.error && (
        <div className="alert alert-error" role="alert">
          {errorMessage(save.error)}
        </div>
      )}
      {saved && (
        <div className="alert alert-ok" role="status">
          Saved.
        </div>
      )}
      <Field label="Location name" error={errors.name?.message}>
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
      <Field
        label="VAT rate (%)"
        error={errors.vatRatePercent?.message}
        hint="Prices are VAT-inclusive. This rate only determines the VAT portion shown on receipts; it is never added on top. Each order keeps the rate it was made with."
      >
        <input type="number" step="0.01" inputMode="decimal" {...register('vatRatePercent', { valueAsNumber: true })} />
      </Field>
      <button className="btn btn-primary" disabled={!isDirty || save.isPending}>
        {save.isPending ? 'Saving…' : 'Save settings'}
      </button>
    </form>
  )
}

export function SettingsPage() {
  const hasLocation = useAuthStore((s) => !!s.user?.location)
  const q = useCurrentLocation()
  const brand = useBranding()
  return (
    <section className="page narrow">
      <PageHeader title="Brand" />
      {brand.isLoading && <Spinner />}
      {brand.error && <ErrorBox error={brand.error} onRetry={() => void brand.refetch()} />}
      {brand.data && <BrandSettings brand={brand.data} />}
      <h2 className="section-title">Location settings</h2>
      {!hasLocation && (
        <p className="muted">
          You are in the All locations view. Pick a location in the header to edit its settings, or manage every
          location under Locations.
        </p>
      )}
      {hasLocation && q.isLoading && <Spinner />}
      {q.error && <ErrorBox error={q.error} onRetry={() => void q.refetch()} />}
      {q.data && (
        <>
          <LocationForm key={q.data.id} loc={q.data} />
          <h2 className="section-title">Staff at {q.data.name}</h2>
          <StaffList locationId={q.data.id} />
        </>
      )}
    </section>
  )
}
