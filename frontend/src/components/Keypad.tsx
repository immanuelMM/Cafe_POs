import { IconBackspace } from './icons'

export type KeypadKey = string | 'Backspace' | 'Clear'

interface Props {
  /** Accessible name of the group, e.g. "Cash keypad". */
  label: string
  onKey: (key: KeypadKey) => void
  /** The bottom-left key: a decimal point for money, "Clear" for a PIN, or nothing. */
  extraKey?: '.' | 'Clear' | null
  disabled?: boolean
}

const DIGITS = ['7', '8', '9', '4', '5', '6', '1', '2', '3']

/** Shared touch keypad for cash entry and PIN entry. Keys are real buttons named "0"-"9", ".", "Clear" and "Backspace". */
export function Keypad({ label, onKey, extraKey = '.', disabled }: Props) {
  const press = (k: KeypadKey) => () => onKey(k)
  return (
    <div className="keypad" role="group" aria-label={label}>
      {DIGITS.map((d) => (
        <button key={d} type="button" className="key" onClick={press(d)} disabled={disabled}>
          {d}
        </button>
      ))}
      {extraKey ? (
        <button type="button" className="key key-aux" onClick={press(extraKey)} disabled={disabled}>
          {extraKey}
        </button>
      ) : (
        <span aria-hidden="true" />
      )}
      <button type="button" className="key" onClick={press('0')} disabled={disabled}>
        0
      </button>
      <button
        type="button"
        className="key key-aux"
        onClick={press('Backspace')}
        disabled={disabled}
        aria-label="Backspace"
      >
        <IconBackspace />
      </button>
    </div>
  )
}
