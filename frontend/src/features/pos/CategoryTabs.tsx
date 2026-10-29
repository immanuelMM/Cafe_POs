import { useRef, type KeyboardEvent } from 'react'
import { useMediaQuery } from '../../lib/useMediaQuery'

interface Props {
  categories: { id: string; name: string }[]
  /** '' = All */
  value: string
  onChange: (id: string) => void
}

/**
 * Category selector. Tablets (medium/expanded/large) show a vertical column that scrolls on its own, because a
 * café can have more than 8 categories; phones show a horizontally scrolling chip row. Layout is CSS; only the
 * aria-orientation follows the same media query. Arrow keys move between tabs (roving tabindex).
 */
export function CategoryTabs({ categories, value, onChange }: Props) {
  const vertical = useMediaQuery('(min-width: 600px) and (min-height: 500px)')
  const listRef = useRef<HTMLDivElement>(null)
  const all = [{ id: '', name: 'All' }, ...categories]

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    const prev = vertical ? 'ArrowUp' : 'ArrowLeft'
    const next = vertical ? 'ArrowDown' : 'ArrowRight'
    if (![prev, next, 'Home', 'End'].includes(e.key)) return
    e.preventDefault()
    const i = Math.max(
      0,
      all.findIndex((c) => c.id === value),
    )
    const target =
      e.key === 'Home'
        ? 0
        : e.key === 'End'
          ? all.length - 1
          : (i + (e.key === next ? 1 : -1) + all.length) % all.length
    onChange(all[target].id)
    listRef.current?.querySelectorAll<HTMLElement>('[role="tab"]')[target]?.focus()
  }

  return (
    <div
      ref={listRef}
      className="pos-cats"
      role="tablist"
      aria-label="Categories"
      aria-orientation={vertical ? 'vertical' : 'horizontal'}
      onKeyDown={onKeyDown}
    >
      {all.map((c) => (
        <button
          key={c.id || 'all'}
          type="button"
          role="tab"
          aria-selected={value === c.id}
          tabIndex={value === c.id ? 0 : -1}
          className={`pos-cat${value === c.id ? ' pos-cat-on' : ''}`}
          onClick={() => onChange(c.id)}
        >
          {c.name}
        </button>
      ))}
    </div>
  )
}
