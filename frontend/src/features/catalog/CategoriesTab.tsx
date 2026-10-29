import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Modal } from '../../components/Modal'
import { ErrorBox, Field, Spinner } from '../../components/ui'
import { errorMessage } from '../../lib/api'
import type { Category } from '../../lib/types'
import { catalogCalls, useCatalogMutation, useCategories } from './catalogApi'

const schema = z.object({
  name: z.string().trim().min(1, 'Name is required').max(100),
  sortOrder: z.number({ invalid_type_error: 'Enter a number' }).int('Whole number'),
  active: z.boolean(),
})
type Values = z.infer<typeof schema>

function CategoryForm({ cat, onClose }: { cat: Category | null; onClose: () => void }) {
  const save = useCatalogMutation(catalogCalls.saveCategory)
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { name: cat?.name ?? '', sortOrder: cat?.sortOrder ?? 0, active: cat?.active ?? true },
  })
  return (
    <Modal title={cat ? 'Edit category' : 'New category'} onClose={onClose}>
      <form
        className="form"
        onSubmit={handleSubmit(async (v) => {
          await save.mutateAsync({ id: cat?.id, body: v }).then(onClose, () => undefined)
        })}
      >
        {save.error && (
          <div className="alert alert-error" role="alert">
            {errorMessage(save.error)}
          </div>
        )}
        <Field label="Name" error={errors.name?.message}>
          <input {...register('name')} />
        </Field>
        <Field label="Sort order" error={errors.sortOrder?.message}>
          <input type="number" inputMode="numeric" {...register('sortOrder', { valueAsNumber: true })} />
        </Field>
        <label className="check">
          <input type="checkbox" {...register('active')} /> Active (visible on POS)
        </label>
        <button className="btn btn-primary btn-block" disabled={save.isPending}>
          {save.isPending ? 'Saving…' : 'Save'}
        </button>
      </form>
    </Modal>
  )
}

export function CategoriesTab() {
  const q = useCategories(true)
  const [editing, setEditing] = useState<Category | 'new' | null>(null)
  const del = useCatalogMutation(catalogCalls.deleteCategory)

  return (
    <div>
      <div className="toolbar">
        <button className="btn btn-primary" onClick={() => setEditing('new')}>
          + New category
        </button>
      </div>
      {q.isLoading && <Spinner />}
      {q.error && <ErrorBox error={q.error} onRetry={() => void q.refetch()} />}
      {del.error && <ErrorBox error={del.error} />}
      <ul className="rows">
        {q.data?.map((c) => (
          <li key={c.id} className="row">
            <div className="grow">
              <strong>{c.name}</strong> <span className="muted">order {c.sortOrder}</span>
            </div>
            <span className={`badge ${c.active ? 'badge-completed' : 'badge-canceled'}`}>
              {c.active ? 'Active' : 'Inactive'}
            </span>
            <button className="btn btn-sm" onClick={() => setEditing(c)}>
              Edit
            </button>
            {c.active && (
              <button
                className="btn btn-sm btn-danger-ghost"
                disabled={del.isPending}
                onClick={() => window.confirm(`Deactivate "${c.name}"?`) && del.mutate(c.id)}
              >
                Deactivate
              </button>
            )}
          </li>
        ))}
      </ul>
      {editing && <CategoryForm cat={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </div>
  )
}
