import { errorMessage } from '../lib/errors'
import { useAuthStore } from '../features/auth/authStore'
import { useCartStore } from '../features/pos/cartStore'
import { useSwitchLocation } from '../features/locations/useLocations'

/** Header control: Owner and multi-location staff can switch, single-location staff see a locked label. */
export function LocationSwitcher() {
  const user = useAuthStore((s) => s.user)
  const cartLines = useCartStore((s) => s.lines.length)
  const sw = useSwitchLocation()
  if (!user) return null

  const options = user.locations
  const canSwitch = user.isOwner || options.length > 1

  if (!canSwitch) {
    return (
      <span className="loc-label" title="Your assigned location">
        <span aria-hidden="true">📍</span> {user.location?.name ?? 'No location'}
      </span>
    )
  }

  const onChange = (value: string) => {
    if (value === (user.location?.id ?? '')) return
    if (cartLines > 0 && !window.confirm('Switching location clears the current order. Continue?')) return
    sw.mutate(value === '' ? null : value)
  }

  return (
    <label className="loc-switch">
      <span className="sr-only">Active location</span>
      <span aria-hidden="true">📍</span>
      <select
        value={user.location?.id ?? ''}
        onChange={(e) => onChange(e.target.value)}
        disabled={sw.isPending}
        aria-label="Active location"
      >
        {user.isOwner && <option value="">All locations</option>}
        {options.map((l) => (
          <option key={l.locationId} value={l.locationId}>
            {l.locationName}
          </option>
        ))}
      </select>
      {sw.isError && (
        <span className="loc-error" role="alert">
          {errorMessage(sw.error)}
        </span>
      )}
    </label>
  )
}
