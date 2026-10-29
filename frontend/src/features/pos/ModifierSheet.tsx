import { useId, useMemo, useState, type ReactNode } from 'react'
import { IconCheck, IconMinus, IconPlus } from '../../components/icons'
import { Modal } from '../../components/Modal'
import { formatMoney, round2 } from '../../lib/format'
import type { Item, ModifierList } from '../../api/types'
import { assetUrl } from '../branding/useBranding'
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
    l.modifiers
      .filter((m) => m.active && (selected[l.id] ?? []).includes(m.id))
      .map((m) => ({ id: m.id, name: m.name, priceDelta: m.priceDelta })),
  )
  const unit = round2(
    (variation ? (variation.effectivePrice ?? variation.price) : 0) + chosen.reduce((s, m) => s + m.priceDelta, 0),
  )

  const submit = () => {
    if (!variation || problems.length) return
    onAdd({
      itemName: item.name,
      variationId: variation.id,
      variationName: variation.name,
      basePrice: variation.effectivePrice ?? variation.price,
      modifiers: chosen,
      quantity: qty,
    })
  }

  return (
    <Modal
      title={item.name}
      onClose={onClose}
      variant="full"
      wide
      className="modal-opts"
      footer={
        <div className="opts-foot">
          {problems.length > 0 && (
            <p className="field-error opts-problem" role="alert">
              Select the required options: {problems.map((p) => p.name).join(', ')}.
            </p>
          )}
          <div className="opts-foot-row">
            <div className="stepper stepper-lg" role="group" aria-label="Quantity">
              <button
                type="button"
                className="btn btn-icon"
                onClick={() => setQty((q) => Math.max(1, q - 1))}
                aria-label="Decrease quantity"
              >
                <IconMinus />
              </button>
              <output aria-live="polite">{qty}</output>
              <button
                type="button"
                className="btn btn-icon"
                onClick={() => setQty((q) => Math.min(MAX_QTY, q + 1))}
                aria-label="Increase quantity"
              >
                <IconPlus />
              </button>
            </div>
            <button
              type="button"
              className="btn btn-primary btn-lg btn-grow"
              disabled={!variation || problems.length > 0}
              onClick={submit}
            >
              Add to order · {formatMoney(unit * qty)}
            </button>
          </div>
        </div>
      }
    >
      {item.imageUrl && <img className="sheet-photo" src={assetUrl(item.imageUrl) ?? ''} alt={item.name} />}
      {item.description && <p className="muted">{item.description}</p>}

      <OptionGroup name="Size / variation" kind="single">
        {variations.map((v) => (
          <OptionChip
            key={v.id}
            type="radio"
            group="variation"
            label={v.name}
            note={formatMoney(v.effectivePrice ?? v.price)}
            on={v.id === variationId}
            onChange={() => setVariationId(v.id)}
          />
        ))}
      </OptionGroup>

      {lists.map((l) => {
        const cur = selected[l.id] ?? []
        const single = l.maxSelections === 1
        const required = l.minSelections > 0
        const missing = cur.length < l.minSelections
        return (
          <OptionGroup
            key={l.id}
            name={l.name}
            kind={single ? 'single' : 'multi'}
            hint={rangeLabel(l)}
            badge={required ? (missing ? 'required' : 'done') : undefined}
          >
            {l.modifiers
              .filter((m) => m.active)
              .map((m) => {
                const on = cur.includes(m.id)
                const full = !single && !on && cur.length >= l.maxSelections
                return (
                  <OptionChip
                    key={m.id}
                    type={single ? 'radio' : 'checkbox'}
                    group={`ml-${l.id}`}
                    label={m.name}
                    note={m.priceDelta !== 0 ? `${m.priceDelta > 0 ? '+' : ''}${formatMoney(m.priceDelta)}` : undefined}
                    on={on}
                    disabled={full}
                    onChange={() => toggle(l, m.id)}
                    onClick={() => single && on && toggle(l, m.id)}
                  />
                )
              })}
          </OptionGroup>
        )
      })}
    </Modal>
  )
}

/** One option group. Single choice = radiogroup, multiple = group of checkboxes; the legend carries name, hint and Required badge. */
function OptionGroup({
  name,
  kind,
  hint,
  badge,
  children,
}: {
  name: string
  kind: 'single' | 'multi'
  hint?: string
  badge?: 'required' | 'done'
  children: ReactNode
}) {
  const id = useId()
  return (
    <fieldset
      className="opt-group"
      role={kind === 'single' ? 'radiogroup' : 'group'}
      aria-labelledby={`${id}-name`}
      aria-describedby={hint ? `${id}-hint` : undefined}
      aria-required={kind === 'single' && badge ? true : undefined}
    >
      <legend>
        <span id={`${id}-name`}>{name}</span>
        {badge === 'required' && <span className="req req-missing">Required</span>}
        {badge === 'done' && (
          <span className="req req-done">
            <IconCheck size={16} /> Selected
          </span>
        )}
        {hint && (
          <span className="req" id={`${id}-hint`}>
            {hint}
          </span>
        )}
      </legend>
      <div className="opt-chips">{children}</div>
    </fieldset>
  )
}

/** Big (>= 56px) tap chip. The native radio/checkbox covers the label, so tapping anywhere selects and screen readers get the real role. */
function OptionChip({
  type,
  group,
  label,
  note,
  on,
  disabled,
  onChange,
  onClick,
}: {
  type: 'radio' | 'checkbox'
  group: string
  label: string
  note?: string
  on: boolean
  disabled?: boolean
  onChange: () => void
  onClick?: () => void
}) {
  return (
    <label className={`opt-chip${on ? ' opt-chip-on' : ''}${disabled ? ' opt-chip-disabled' : ''}`}>
      <input type={type} name={group} checked={on} disabled={disabled} onChange={onChange} onClick={onClick} />
      <span className="opt-chip-label">
        {on && <IconCheck size={20} />}
        {label}
      </span>
      {note && <small>{note}</small>}
    </label>
  )
}
