import { useState } from 'react'
import { useFieldArray, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Modal } from '../../components/Modal'
import { ErrorBox, Field, Spinner } from '../../components/ui'
import { errorMessage } from '../../lib/api'
import { formatMoney } from '../../lib/format'
import type { Item, Variation } from '../../lib/types'
import { catalogCalls, useCatalogMutation, useCategories, useItems, useModifierLists } from './catalogApi'
import { ItemPhotoSection, ItemThumb } from './ItemPhotoSection'
import { OverridesEditor } from './OverridesEditor'
import { RecipeEditor } from './RecipeEditor'

const num = (msg = 'Enter a number') => z.number({ invalid_type_error: msg })

const variationSchema = z.object({
  name: z.string().trim().min(1, 'Name required'),
  sku: z.string().trim().min(1, 'SKU required'),
  price: num().min(0, 'Must be 0 or more'),
})

const itemSchema = z.object({
  categoryId: z.string().min(1, 'Choose a category'),
  name: z.string().trim().min(1, 'Name is required'),
  description: z.string().trim().optional(),
  sortOrder: num().int(),
  active: z.boolean(),
  modifierListIds: z.array(z.string()),
  variations: z.array(variationSchema),
})
type ItemValues = z.infer<typeof itemSchema>

/** Edit one existing variation (PUT/DELETE /variations/{id}) and open its recipe. */
function VariationRow({
  v,
  onRecipe,
  onOverrides,
}: {
  v: Variation
  onRecipe: (v: Variation) => void
  onOverrides: (v: Variation) => void
}) {
  const save = useCatalogMutation(catalogCalls.saveVariation)
  const del = useCatalogMutation(catalogCalls.deleteVariation)
  const {
    register,
    handleSubmit,
    formState: { errors, isDirty },
  } = useForm<z.infer<typeof variationSchema> & { active: boolean }>({
    resolver: zodResolver(variationSchema.extend({ active: z.boolean() })),
    defaultValues: { name: v.name, sku: v.sku, price: v.price, active: v.active },
  })
  return (
    <form
      className="var-row"
      onSubmit={handleSubmit((vals) => save.mutate({ id: v.id, itemId: v.itemId, body: vals }))}
    >
      <Field label="Name" error={errors.name?.message}>
        <input {...register('name')} />
      </Field>
      <Field label="SKU" error={errors.sku?.message}>
        <input {...register('sku')} />
      </Field>
      <Field label="Price (₱, VAT incl.)" error={errors.price?.message}>
        <input type="number" step="0.01" inputMode="decimal" {...register('price', { valueAsNumber: true })} />
      </Field>
      <label className="check">
        <input type="checkbox" {...register('active')} /> Active
      </label>
      <div className="var-actions">
        <button className="btn btn-sm btn-primary" disabled={!isDirty || save.isPending}>
          Save
        </button>
        <button type="button" className="btn btn-sm" onClick={() => onRecipe(v)}>
          Recipe
        </button>
        <button type="button" className="btn btn-sm" onClick={() => onOverrides(v)}>
          Location prices
        </button>
        <button
          type="button"
          className="btn btn-sm btn-danger-ghost"
          disabled={del.isPending}
          onClick={() => window.confirm(`Deactivate variation "${v.name}"?`) && del.mutate(v.id)}
        >
          Deactivate
        </button>
      </div>
      {(save.error || del.error) && (
        <div className="alert alert-error var-err" role="alert">
          {errorMessage(save.error ?? del.error)}
        </div>
      )}
    </form>
  )
}

function NewVariation({ itemId }: { itemId: string }) {
  const save = useCatalogMutation(catalogCalls.saveVariation)
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<z.infer<typeof variationSchema>>({
    resolver: zodResolver(variationSchema),
    defaultValues: { name: '', sku: '', price: 0 },
  })
  return (
    <form
      className="var-row var-new"
      onSubmit={handleSubmit(async (vals) => {
        await save.mutateAsync({ itemId, body: vals }).then(
          () => reset(),
          () => undefined,
        )
      })}
    >
      <Field label="New variation" error={errors.name?.message}>
        <input placeholder="e.g. Large" {...register('name')} />
      </Field>
      <Field label="SKU" error={errors.sku?.message}>
        <input {...register('sku')} />
      </Field>
      <Field label="Price (₱)" error={errors.price?.message}>
        <input type="number" step="0.01" inputMode="decimal" {...register('price', { valueAsNumber: true })} />
      </Field>
      <div className="var-actions">
        <button className="btn btn-sm" disabled={save.isPending}>
          + Add variation
        </button>
      </div>
      {save.error && (
        <div className="alert alert-error var-err" role="alert">
          {errorMessage(save.error)}
        </div>
      )}
    </form>
  )
}

function ItemForm({ initial, onClose }: { initial: Item | null; onClose: () => void }) {
  const itemId = initial?.id ?? null
  const cats = useCategories(true)
  const lists = useModifierLists(true)
  // Read the live item from the cache-backed list so variation edits show up without closing.
  const items = useItems({ includeInactive: true })
  const item = (itemId ? items.data?.data.find((i) => i.id === itemId) : undefined) ?? initial ?? undefined
  const save = useCatalogMutation(catalogCalls.saveItem)
  const [recipeFor, setRecipeFor] = useState<Variation | null>(null)
  const [overridesFor, setOverridesFor] = useState<Variation | null>(null)

  const {
    register,
    control,
    handleSubmit,
    formState: { errors },
  } = useForm<ItemValues>({
    resolver: zodResolver(
      itemSchema.refine((v) => itemId !== null || v.variations.length > 0, {
        path: ['variations'],
        message: 'Add at least one variation',
      }),
    ),
    defaultValues: {
      categoryId: item?.categoryId ?? '',
      name: item?.name ?? '',
      description: item?.description ?? '',
      sortOrder: item?.sortOrder ?? 0,
      active: item?.active ?? true,
      modifierListIds: item?.modifierListIds ?? [],
      variations: itemId ? [] : [{ name: 'Regular', sku: '', price: 0 }],
    },
  })
  const { fields, append, remove } = useFieldArray({ control, name: 'variations' })

  const submit = async (v: ItemValues) => {
    const body = {
      categoryId: v.categoryId,
      name: v.name,
      description: v.description || null,
      sortOrder: v.sortOrder,
      active: v.active,
      modifierListIds: v.modifierListIds,
      ...(itemId ? {} : { variations: v.variations }),
    }
    await save.mutateAsync({ id: itemId ?? undefined, body }).then(onClose, () => undefined)
  }

  return (
    <>
      <Modal title={itemId ? 'Edit item' : 'New item'} onClose={onClose} wide>
        <form className="form" onSubmit={handleSubmit(submit)}>
          {save.error && (
            <div className="alert alert-error" role="alert">
              {errorMessage(save.error)}
            </div>
          )}
          <div className="grid-2">
            <Field label="Name" error={errors.name?.message}>
              <input {...register('name')} />
            </Field>
            <Field label="Category" error={errors.categoryId?.message}>
              <select {...register('categoryId')}>
                <option value="">Select…</option>
                {cats.data?.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                    {!c.active && ' (inactive)'}
                  </option>
                ))}
              </select>
            </Field>
          </div>
          <Field label="Description" error={errors.description?.message}>
            <input {...register('description')} />
          </Field>
          <div className="grid-2">
            <Field label="Sort order" error={errors.sortOrder?.message}>
              <input type="number" inputMode="numeric" {...register('sortOrder', { valueAsNumber: true })} />
            </Field>
            <label className="check">
              <input type="checkbox" {...register('active')} /> Active (sellable)
            </label>
          </div>
          <fieldset className="opt-group">
            <legend>Modifier lists</legend>
            <div className="chips">
              {lists.data
                ?.filter((l) => l.active)
                .map((l) => (
                  <label key={l.id} className="check chip-check">
                    <input type="checkbox" value={l.id} {...register('modifierListIds')} /> {l.name}
                  </label>
                ))}
              {lists.data?.length === 0 && <span className="muted">No modifier lists yet.</span>}
            </div>
          </fieldset>

          {!itemId && (
            <fieldset className="opt-group">
              <legend>Variations</legend>
              {fields.map((f, i) => (
                <div key={f.id} className="var-row">
                  <Field label="Name" error={errors.variations?.[i]?.name?.message}>
                    <input {...register(`variations.${i}.name`)} />
                  </Field>
                  <Field label="SKU" error={errors.variations?.[i]?.sku?.message}>
                    <input {...register(`variations.${i}.sku`)} />
                  </Field>
                  <Field label="Price (₱, VAT incl.)" error={errors.variations?.[i]?.price?.message}>
                    <input
                      type="number"
                      step="0.01"
                      inputMode="decimal"
                      {...register(`variations.${i}.price`, { valueAsNumber: true })}
                    />
                  </Field>
                  {fields.length > 1 && (
                    <button
                      type="button"
                      className="btn btn-icon btn-danger-ghost"
                      onClick={() => remove(i)}
                      aria-label="Remove variation"
                    >
                      🗑
                    </button>
                  )}
                </div>
              ))}
              {errors.variations?.message && <p className="field-error">{errors.variations.message}</p>}
              {errors.variations?.root?.message && <p className="field-error">{errors.variations.root.message}</p>}
              <button type="button" className="btn" onClick={() => append({ name: '', sku: '', price: 0 })}>
                + Add variation
              </button>
              <p className="field-hint">Set recipes after the item is created.</p>
            </fieldset>
          )}
          <button className="btn btn-primary btn-block" disabled={save.isPending}>
            {save.isPending ? 'Saving…' : itemId ? 'Save item' : 'Create item'}
          </button>
        </form>

        {itemId && item && <ItemPhotoSection item={item} />}
        {!itemId && <p className="field-hint">Save the item first to add a photo.</p>}

        {itemId && item && (
          <fieldset className="opt-group">
            <legend>Variations</legend>
            {item.variations.map((v) => (
              <VariationRow
                key={`${v.id}-${v.name}-${v.sku}-${v.price}-${v.active}`}
                v={v}
                onRecipe={setRecipeFor}
                onOverrides={setOverridesFor}
              />
            ))}
            <NewVariation itemId={itemId} />
          </fieldset>
        )}
      </Modal>
      {recipeFor && <RecipeEditor variation={recipeFor} onClose={() => setRecipeFor(null)} />}
      {overridesFor && <OverridesEditor variation={overridesFor} onClose={() => setOverridesFor(null)} />}
    </>
  )
}

export function ItemsTab() {
  const cats = useCategories(true)
  const [categoryId, setCategoryId] = useState('')
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<Item | 'new' | null>(null)
  const items = useItems({
    categoryId: categoryId || undefined,
    search: search.trim() || undefined,
    includeInactive: true,
  })
  const del = useCatalogMutation(catalogCalls.deleteItem)
  const catName = (id: string) => cats.data?.find((c) => c.id === id)?.name ?? '—'

  return (
    <div>
      <div className="toolbar">
        <select aria-label="Filter by category" value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
          <option value="">All categories</option>
          {cats.data?.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
        <input
          type="search"
          placeholder="Search…"
          aria-label="Search items"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <button className="btn btn-primary" onClick={() => setEditing('new')}>
          + New item
        </button>
      </div>
      {items.isLoading && <Spinner />}
      {items.error && <ErrorBox error={items.error} onRetry={() => void items.refetch()} />}
      {del.error && <ErrorBox error={del.error} />}
      <ul className="rows">
        {items.data?.data.map((it: Item) => (
          <li key={it.id} className="row">
            <ItemThumb item={it} />
            <div className="grow">
              <strong>{it.name}</strong> <span className="muted">{catName(it.categoryId)}</span>
              <div className="muted">
                {it.variations
                  .filter((v) => v.active)
                  .map((v) => `${v.name} ${formatMoney(v.price)}`)
                  .join(' · ')}
              </div>
            </div>
            <span className={`badge ${it.active ? 'badge-completed' : 'badge-canceled'}`}>
              {it.active ? 'Active' : 'Inactive'}
            </span>
            <button className="btn btn-sm" onClick={() => setEditing(it)}>
              Edit
            </button>
            {it.active && (
              <button
                className="btn btn-sm btn-danger-ghost"
                disabled={del.isPending}
                onClick={() => window.confirm(`Deactivate "${it.name}"?`) && del.mutate(it.id)}
              >
                Deactivate
              </button>
            )}
          </li>
        ))}
      </ul>
      {editing && <ItemForm initial={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </div>
  )
}
