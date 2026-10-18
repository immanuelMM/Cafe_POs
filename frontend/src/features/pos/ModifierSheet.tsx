import { useMemo, useState } from 'react'
import { Modal } from '../../components/Modal'
import { formatMoney, round2 } from '../../lib/format'
import type { Item, ModifierList } from '../../lib/types'
import { MAX_QTY, type CartLine } from './cartStore'

interface Props {
  item: Item
  lists: ModifierList[] // active lists linked to this item
  onAdd: (line: Omit<CartLine, 'key'>) => void
  onClose: () => void
}

const rangeLabel = (l: ModifierList) =>
  l.minSelections > 0
    ? l.minSelections === l.maxSelections
      ? `Choose ${l.minSelections}`
      : `Choose ${l.minSelections}–${l.maxSelections}`
    : `Optional, up to ${l.maxSelections}`

export function ModifierSheet({ item, lists, onAdd, onClose }: Props) {
  const variations = item.variations.filter((v) => v.active)
  const [variationId, setVariationId] = useState(variations[0]?.id ?? '')
  const [selected, setSelected] = useState<Record<string, string[]>>({}) // listId -> modifier ids
  const [qty, setQty] = useState(1)

  const variation = variations.find((v) => v.id === variationId)

  const toggle = (list: ModifierList, modId: string) =>
    setSelected((s) => {
      const cur = s[list.id] ?? []
      if (cur.includes(modId)) return { ...s, [list.id]: cur.filter((x) => x !== modId) }
      if (list.maxSelections === 1) return { ...s, [list.id]: [modId] } // radio behaviour
      if (cur.length >= list.maxSelections) return s
      return { ...s, [list.id]: [...cur, modId] }
    })

  const problems = useMemo(
    () => lists.filter((l) => (selected[l.id]?.length ?? 0) < l.minSelections),
    [lists, selected],
  )

  const chosen = lists.flatMap((l) =>
    l.modifiers.filter((m) => m.active && (selected[l.id] ?? []).includes(m.id)).map((m) => ({ id: m.id, name: m.name, priceDelta: m.priceDelta })),
  )
  const unit = round2((variation?.price ?? 0) + chosen.reduce((s, m) => s + m.priceDelta, 0))

  const submit = () => {
    if (!variation || problems.length) return
    onAdd({
      itemName: item.name,
      variationId: variation.id,
      variationName: variation.name,
      basePrice: variation.price,
      modifiers: chosen,
      quantity: qty,
    })
  }

  return (
    <Modal
      title={item.name}
      onClose={onClose}
      footer={
        <>
          <div className="stepper" role="group" aria-label="Quantity">
            <button type="button" className="btn btn-icon" onClick={() => setQty((q) => Math.max(1, q - 1))} aria-label="Decrease quantity">
              −
            </button>
            <output aria-live="polite">{qty}</output>
            <button type="button" className="btn btn-icon" onClick={() => setQty((q) => Math.min(MAX_QTY, q + 1))} aria-label="Increase quantity">
              +
            </button>
          </div>
          <button type="button" className="btn btn-primary btn-grow" disabled={!variation || problems.length > 0} onClick={submit}>
            Add · {formatMoney(unit * qty)}
          </button>
        </>
      }
    >
      {item.description && <p className="muted">{item.description}</p>}

      <fieldset className="opt-group">
        <legend>Size / variation</legend>
        <div className="chips">
          {variations.map((v) => (
            <label key={v.id} className={`chip${v.id === variationId ? ' chip-on' : ''}`}>
              <input type="radio" name="variation" checked={v.id === variationId} onChange={() => setVariationId(v.id)} />
              <span>{v.name}</span>
              <small>{formatMoney(v.price)}</small>
            </label>
          ))}
        </div>
      </fieldset>

      {lists.map((l) => {
        const cur = selected[l.id] ?? []
        const single = l.maxSelections === 1
        const missing = cur.length < l.minSelections
        return (
          <fieldset className="opt-group" key={l.id}>
            <legend>
              {l.name}
              <span className={`req${missing ? ' req-missing' : ''}`}>{rangeLabel(l)}</span>
            </legend>
            <div className="chips">
              {l.modifiers
                .filter((m) => m.active)
                .map((m) => {
                  const on = cur.includes(m.id)
                  const full = !single && !on && cur.length >= l.maxSelections
                  return (
                    <label key={m.id} className={`chip${on ? ' chip-on' : ''}${full ? ' chip-disabled' : ''}`}>
                      <input
                        type={single ? 'radio' : 'checkbox'}
                        name={`ml-${l.id}`}
                        checked={on}
                        disabled={full}
                        onChange={() => toggle(l, m.id)}
                        onClick={() => single && on && toggle(l, m.id)}
                      />
                      <span>{m.name}</span>
                      {m.priceDelta !== 0 && (
                        <small>
                          {m.priceDelta > 0 ? '+' : ''}
                          {formatMoney(m.priceDelta)}
                        </small>
                      )}
                    </label>
                  )
                })}
            </div>
          </fieldset>
        )
      })}
      {problems.length > 0 && (
        <p className="field-error" role="alert">
          Select the required options: {problems.map((p) => p.name).join(', ')}.
        </p>
      )}
    </Modal>
  )
}
