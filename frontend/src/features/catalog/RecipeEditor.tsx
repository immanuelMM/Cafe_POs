import { useState } from 'react'
import { Modal } from '../../components/Modal'
import { ErrorBox, Spinner } from '../../components/ui'
import { errorMessage } from '../../lib/errors'
import type { Variation } from '../../api/types'
import { useIngredients } from '../inventory/useInventory'
import { catalogCalls, useCatalogMutation, useRecipe } from './useCatalog'

interface Line {
  ingredientId: string
  quantity: string
}

function Editor({ variation, initial, onClose }: { variation: Variation; initial: Line[]; onClose: () => void }) {
  const ings = useIngredients()
  const [lines, setLines] = useState<Line[]>(initial)
  const save = useCatalogMutation(catalogCalls.saveRecipe, ['recipe'])

  const unitOf = (id: string) => ings.data?.find((i) => i.id === id)?.unit ?? ''
  const used = new Set(lines.map((l) => l.ingredientId))
  const invalid = lines.some((l) => !l.ingredientId || !(Number(l.quantity) > 0)) || used.size !== lines.length

  const update = (i: number, patch: Partial<Line>) =>
    setLines((ls) => ls.map((l, idx) => (idx === i ? { ...l, ...patch } : l)))

  return (
    <Modal
      title={`Recipe: ${variation.name}`}
      onClose={onClose}
      footer={
        <button
          className="btn btn-primary btn-grow"
          disabled={invalid || save.isPending}
          onClick={() =>
            save
              .mutateAsync({
                variationId: variation.id,
                lines: lines.map((l) => ({ ingredientId: l.ingredientId, quantity: Number(l.quantity) })),
              })
              .then(onClose, () => undefined)
          }
        >
          {save.isPending ? 'Saving…' : 'Save recipe'}
        </button>
      }
    >
      <p className="muted">
        Ingredients used for ONE unit sold, in each ingredient&apos;s own unit. Stock is deducted when an order is
        completed.
      </p>
      {ings.isLoading && <Spinner />}
      {ings.error && <ErrorBox error={ings.error} />}
      {save.error && (
        <div className="alert alert-error" role="alert">
          {errorMessage(save.error)}
        </div>
      )}
      {lines.map((l, i) => (
        <div key={i} className="recipe-row">
          <label className="field">
            <span className="field-label">Ingredient</span>
            <select value={l.ingredientId} onChange={(e) => update(i, { ingredientId: e.target.value })}>
              <option value="">Select…</option>
              {ings.data?.map((ing) => (
                <option key={ing.id} value={ing.id} disabled={used.has(ing.id) && ing.id !== l.ingredientId}>
                  {ing.name} ({ing.unit})
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span className="field-label">Qty {unitOf(l.ingredientId) && `(${unitOf(l.ingredientId)})`}</span>
            <input
              type="number"
              step="0.001"
              min="0"
              inputMode="decimal"
              value={l.quantity}
              onChange={(e) => update(i, { quantity: e.target.value })}
            />
          </label>
          <button
            className="btn btn-icon btn-danger-ghost"
            onClick={() => setLines((ls) => ls.filter((_, idx) => idx !== i))}
            aria-label="Remove line"
          >
            🗑
          </button>
        </div>
      ))}
      <button className="btn" onClick={() => setLines((ls) => [...ls, { ingredientId: '', quantity: '' }])}>
        + Add ingredient
      </button>
      {lines.length === 0 && <p className="muted">No ingredients: selling this variation will not change stock.</p>}
    </Modal>
  )
}

export function RecipeEditor({ variation, onClose }: { variation: Variation; onClose: () => void }) {
  const recipe = useRecipe(variation.id)
  if (recipe.isLoading)
    return (
      <Modal title={`Recipe: ${variation.name}`} onClose={onClose}>
        <Spinner />
      </Modal>
    )
  if (recipe.error || !recipe.data)
    return (
      <Modal title={`Recipe: ${variation.name}`} onClose={onClose}>
        <ErrorBox error={recipe.error} onRetry={() => void recipe.refetch()} />
      </Modal>
    )
  return (
    <Editor
      variation={variation}
      initial={recipe.data.lines.map((l) => ({ ingredientId: l.ingredientId, quantity: String(l.quantity) }))}
      onClose={onClose}
    />
  )
}
