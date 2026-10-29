import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { CategoryTabs } from './CategoryTabs'

const cats = [
  { id: 'a', name: 'Tea' },
  { id: 'b', name: 'Pastry' },
]

describe('CategoryTabs roving tabindex', () => {
  it('makes only the selected tab a tab stop', () => {
    render(<CategoryTabs categories={cats} value="b" onChange={() => undefined} />)
    expect(screen.getByRole('tab', { name: 'Pastry' })).toHaveAttribute('tabindex', '0')
    expect(screen.getByRole('tab', { name: 'Tea' })).toHaveAttribute('tabindex', '-1')
    expect(screen.getByRole('tab', { name: 'All' })).toHaveAttribute('tabindex', '-1')
  })

  it('falls back to All as the tab stop when the value matches no category (deactivated or stale)', () => {
    render(<CategoryTabs categories={cats} value="gone" onChange={() => undefined} />)
    const stops = screen.getAllByRole('tab').filter((t) => t.getAttribute('tabindex') === '0')
    expect(stops.map((t) => t.textContent)).toEqual(['All'])
  })
})
