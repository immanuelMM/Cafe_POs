import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { ErrorBox, PageHeader, Spinner } from '../../components/ui'
import { ApiError, errorMessage } from '../../lib/errors'
import { formatTime } from '../../lib/format'
import type { TimeClockStaff } from '../../api/types'
import { useAuthStore } from '../auth/authStore'
import { useCurrentLocation } from '../locations/useLocations'
import { PinModal } from './PinModal'
import { usePunch, useSetMyPin, useTimeClockStaff } from './useTimeClock'
import './timeclock.css'

const punchError = (e: unknown) => {
  if (e instanceof ApiError) {
    if (e.status === 403) return 'Incorrect PIN'
    if (e.status === 423) return 'Too many attempts, try again in 5 minutes'
    if (e.status === 422) return 'No PIN set — ask an Admin'
    if (e.status === 404) return 'This person is not assigned to this location.'
  }
  return errorMessage(e)
}

function useFullscreen() {
  const [on, setOn] = useState(!!document.fullscreenElement)
  useEffect(() => {
    const h = () => setOn(!!document.fullscreenElement)
    document.addEventListener('fullscreenchange', h)
    return () => document.removeEventListener('fullscreenchange', h)
  }, [])
  const toggle = () => {
    if (document.fullscreenElement) void document.exitFullscreen()
    else void document.documentElement.requestFullscreen?.().catch(() => undefined)
  }
  return { on, toggle, supported: !!document.documentElement.requestFullscreen }
}

export function TimeClockPage() {
  const hasLocation = useAuthStore((s) => !!s.user?.location)
  const tz = useCurrentLocation().data?.timeZone
  const staff = useTimeClockStaff()
  const punch = usePunch()
  const myPin = useSetMyPin()
  const fs = useFullscreen()
  const [target, setTarget] = useState<TimeClockStaff | null>(null)
  const [settingPin, setSettingPin] = useState(false)
  const [banner, setBanner] = useState<string | null>(null)
  const [pinSaved, setPinSaved] = useState(false)

  // Confirmation auto-dismisses after ~3 s.
  useEffect(() => {
    if (!banner) return
    const t = setTimeout(() => setBanner(null), 3000)
    return () => clearTimeout(t)
  }, [banner])

  if (!hasLocation) {
    return (
      <section className="page narrow">
        <PageHeader title="Time clock" />
        <p className="muted">
          The time clock works per location. Pick a location in the header (you are in the All locations view).
        </p>
      </section>
    )
  }

  const closePin = () => {
    setTarget(null)
    punch.reset()
  }
  const submitPunch = (pin: string) => {
    if (!target) return
    punch.mutate(
      { userId: target.userId, pin },
      {
        onSuccess: (r) => {
          const first = r.entry.displayName.split(' ')[0]
          setBanner(
            r.action === 'In'
              ? `Clocked IN at ${formatTime(r.entry.clockInAt, tz)}, ${first}`
              : `Clocked OUT, ${r.entry.hours ?? 0} h worked, ${first}`,
          )
          closePin()
        },
      },
    )
  }

  return (
    <section className="page">
      {banner && (
        <div className="clock-banner" role="status">
          {banner}
        </div>
      )}
      <PageHeader title="Time clock">
        <Link className="btn" to="/timeclock/records">
          Time records
        </Link>
        <button
          type="button"
          className="btn"
          onClick={() => {
            setPinSaved(false)
            setSettingPin(true)
          }}
        >
          Set my PIN
        </button>
        {fs.supported && (
          <button type="button" className="btn" onClick={fs.toggle}>
            {fs.on ? 'Exit full screen' : 'Full screen'}
          </button>
        )}
      </PageHeader>
      {pinSaved && (
        <div className="alert alert-ok" role="status">
          Your PIN was saved.
        </div>
      )}
      <p className="muted">Tap your name, then enter your PIN to clock in or out.</p>
      {staff.isLoading && <Spinner label="Loading staff…" />}
      {staff.error && <ErrorBox error={staff.error} onRetry={() => void staff.refetch()} />}
      {staff.data && staff.data.length === 0 && <p className="muted">No staff are assigned to this location.</p>}
      <div className="clock-grid">
        {staff.data?.map((s) => (
          <button
            key={s.userId}
            type="button"
            className={`clock-tile${s.clockedIn ? ' clock-tile-in' : ''}`}
            disabled={!s.hasPin}
            onClick={() => setTarget(s)}
          >
            <span className="clock-name">{s.displayName}</span>
            <small className="muted">{s.role}</small>
            {!s.hasPin ? (
              <span className="badge badge-canceled">No PIN</span>
            ) : s.clockedIn ? (
              <span className="badge badge-completed">In since {formatTime(s.openSince, tz)}</span>
            ) : (
              <span className="badge badge-canceled">Out</span>
            )}
          </button>
        ))}
      </div>

      {target && (
        <PinModal
          title="Clock in / out"
          subtitle={target.displayName}
          busy={punch.isPending}
          error={punch.error ? punchError(punch.error) : null}
          onSubmit={submitPunch}
          onClose={closePin}
        />
      )}
      {settingPin && (
        <PinModal
          title="Set my PIN"
          confirm
          busy={myPin.isPending}
          error={myPin.error ? errorMessage(myPin.error) : null}
          onSubmit={(pin) =>
            myPin.mutate(pin, {
              onSuccess: () => {
                setSettingPin(false)
                setPinSaved(true)
                void staff.refetch()
              },
            })
          }
          onClose={() => {
            setSettingPin(false)
            myPin.reset()
          }}
        />
      )}
    </section>
  )
}
