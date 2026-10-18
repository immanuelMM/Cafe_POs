import { useState } from 'react'
import { useFieldArray, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Modal } from '../../components/Modal'
import { ErrorBox, Field, Spinner } from '../../components/ui'
import { errorMessage } from '../../lib/api'
import { formatMoney } from '../../lib/format'
import type { ModifierList } from '../../lib/types'
import { catalogCalls, useCatalogMutation, useModifierLists } from './catalogApi'

const num = (msg = 'Enter a number') => z.number({ invalid_type_error: msg })

const schema = z
  .object({
    name: z.string().trim().min(1, 'Name is required'),
    minSelections: num().int().min(0),
    maxSelections: num().int().min(1, 'At least 1'),
    active: z.boolean(),
    modifiers: z
      .array(
        z.object({
          id: z.string().optional(),
          name: z.string().trim().min(1, 'Name required'),
          priceDelta: num(),
          active: z.boolean(),
        }),
      )
      .min(1, 'Add at least one modifier'),
  })
  .refine((v) => v.minSelections <= v.maxSelections, { path: ['minSelections'], message: 'Min cannot exceed max' })
type Values = z.infer<typeof schema>

function ModifierListForm({ list, onClose }: { list: ModifierList | null; onClose: () => void }) {
  const save = useCatalogMutation(catalogCalls.saveModifierList, ['recipe'])
  const {
    register,
    control,
    handleSubmit,
    formState: { errors },
  } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: list?.name ?? '',
      minSelections: list?.minSelections ?? 0,
      maxSelections: list?.maxSelections ?? 1,
      active: list?.active ?? true,
      modifiers: list?.modifiers.filter((m) => m.active).map((m) => ({ id: m.id, name: m.name, priceDelta: m.priceDelta, active: true })) ?? [
        { name: '', priceDelta: 0, active: true },
      ],
    },
  })
  const { fields, append, remove } = useFieldArray({ control, name: 'modifiers' })

  return (
    <Modal title={list ? 'Edit modifier list' : 'New modifier list'} onClose={onClose} wide>
      <form
        className="form"
        onSubmit={handleSubmit(async (v) => {
          await save.mutateAsync({ id: list?.id, body: v }).then(onClose, () => undefined)
        })}
      >
        {save.error && <div className="alert alert-error" role="alert">{errorMessage(save.error)}</div>}
        <Field label="List name (e.g. Milk Type)" error={errors.name?.message}>
          <input {...register('name')} />
        </Field>
        <div className="grid-2">
          <Field label="Min selections" error={errors.minSelections?.message} hint="0 = optional">
            <input type="number" inputMode="numeric" {...register('minSelections', { valueAsNumber: true })} />
          </Field>
          <Field label="Max selections" error={errors.maxSelections?.message}>
            <input type="number" inputMode="numeric" {...register('maxSelections', { valueAsNumber: true })} />
          </Field>
        </div>
        <label className="check">
          <input type="checkbox" {...register('active')} /> Active
        </label>

        <fieldset className="opt-group">
          <legend>Modifiers</legend>
          {fields.map((f, i) => (
            <div key={f.id} className="mod-row">
              <Field label="Name" error={errors.modifiers?.[i]?.name?.message}>
                <input {...register(`modifiers.${i}.name`)} />
              </Field>
              <Field label="Price change (₱, VAT incl.)" error={errors.modifiers?.[i]?.priceDelta?.message}>
                <input type="number" step="0.01" inputMode="decimal" {...register(`modifiers.${i}.priceDelta`, { valueAsNumber: true })} />
              </Field>
              <button type="button" className="btn btn-icon btn-danger-ghost" onClick={() => remove(i)} aria-label="Remove modifier">
                🗑
              </button>
            </div>
          ))}
          {errors.modifiers?.message && <p className="field-error">{errors.modifiers.message}</p>}
          <button type="button" className="btn" onClick={() => append({ name: '', priceDelta: 0, active: true })}>
            + Add modifier
          </button>
          <p className="field-hint">Removing a modifier deactivates it; existing orders are unaffected.</p>
        </fieldset>
        <button className="btn btn-primary btn-block" disabled={save.isPending}>
          {save.isPending ? 'Saving…' : 'Save'}
        </button>
      </form>
    </Modal>
  )
}

export function ModifiersTab() {
  const q = useModifierLists(true)
  const [editing, setEditing] = useState<ModifierList | 'new' | null>(null)
  return (
    <div>
      <div className="toolbar">
        <button className="btn btn-primary" onClick={() => setEditing('new')}>
          + New modifier list
        </button>
      </div>
      {q.isLoading && <Spinner />}
      {q.error && <ErrorBox error={q.error} onRetry={() => void q.refetch()} />}
      <ul className="rows">
        {q.data?.map((l) => (
          <li key={l.id} className="row">
            <div className="grow">
              <strong>{l.name}</strong>{' '}
              <span className="muted">
                {l.minSelections}–{l.maxSelections} selections
              </span>
              <div className="muted">
                {l.modifiers
                  .filter((m) => m.active)
                  .map((m) => `${m.name}${m.priceDelta ? ` (${m.priceDelta > 0 ? '+' : ''}${formatMoney(m.priceDelta)})` : ''}`)
                  .join(' · ')}
              </div>
            </div>
            <span className={`badge ${l.active ? 'badge-completed' : 'badge-canceled'}`}>{l.active ? 'Active' : 'Inactive'}</span>
            <button className="btn btn-sm" onClick={() => setEditing(l)}>
              Edit
            </button>
          </li>
        ))}
      </ul>
      {editing && <ModifierListForm list={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </div>
  )
}
