import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Keypad } from './Keypad'

describe('Keypad', () => {
  it('is a labelled group of buttons named 0-9, the extra key and Backspace', () => {
    render(<Keypad label="Cash keypad" onKey={() => undefined} />)
    const group = screen.getByRole('group', { name: 'Cash keypad' })
    expect(group).toBeInTheDocument()
    for (const k of ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', 'Backspace']) {
      expect(screen.getByRole('button', { name: k })).toBeInTheDocument()
    }
  })

  it('reports each pressed key', async () => {
    const onKey = vi.fn()
    const user = userEvent.setup()
    render(<Keypad label="PIN keypad" extraKey="Clear" onKey={onKey} />)
    await user.click(screen.getByRole('button', { name: '7' }))
    await user.click(screen.getByRole('button', { name: 'Backspace' }))
    await user.click(screen.getByRole('button', { name: 'Clear' }))
    expect(onKey.mock.calls.map((c) => c[0])).toEqual(['7', 'Backspace', 'Clear'])
    expect(screen.queryByRole('button', { name: '.' })).not.toBeInTheDocument()
  })
})
