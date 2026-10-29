import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { ErrorBox, Field, PageHeader, Spinner } from '../../components/ui'
import { formatDate, formatTime } from '../../lib/format'
import { addDays, startOfDayUtc, todayIn, weekdayIndex } from '../../lib/zonedTime'
import { isManager, useAuthStore } from '../auth/authStore'
import { useCurrentLocation } from '../locations/useLocations'
import { useTimeClockStaff, useTimeEntries } from './timeclockApi'

type Preset = 'today' | 'week' | '7d' | 'month' | 'custom'
const PRESETS: { id: Preset; label: string }[] = [
  { id: 'today', label: 'Today' },
  { id: 'week', label: 'This week' },
  { id: '7d', label: 'Last 7 days' },
  { id: 'month', label: 'This month' },
  { id: 'custom', label: 'Custom' },
]

const hoursText = (h: number) => `${h.toFixed(2)} h`

function RecordsView({ tz }: { tz: string | undefined }) {
  const manager = useAuthStore((s) => isManager(s.user))
  const today = todayIn(tz)
  const [preset, setPreset] = useState<Preset>('week')
  const [customFrom, setCustomFrom] = useState(today)
  const [customTo, setCustomTo] = useState(today)
  const [userId, setUserId] = useState('')
  const staff = useTimeClockStaff()

  const [fromDate, toDate] = useMemo((): [string, string] => {
    switch (preset) {
      case 'today':
        return [today, today]
      case 'week':
        return [addDays(today, -weekdayIndex(today)), today]
      case '7d':
        return [addDays(today, -6), today]
      case 'month':
        return [`${today.slice(0, 8)}01`, today]
      default:
        return [customFrom, customTo]
    }
  }, [preset, today, customFrom, customTo])
  const validRange = !!fromDate && !!toDate && fromDate <= toDate

  // Local-day boundaries as UTC instants; `to` is exclusive (start of the day after the last day).
  const from = validRange ? startOfDayUtc(fromDate, tz) : ''
  const to = validRange ? startOfDayUtc(addDays(toDate, 1), tz) : ''
  const q = useTimeEntries({ from, to, userId: manager ? userId : '' })
  const entries = useMemo(() => (validRange ? (q.data ?? []) : []), [validRange, q.data])

  const totals = useMemo(() => {
    const m = new Map<string, { name: string; hours: number; open: number }>()
    for (const e of entries) {
      const t = m.get(e.userId) ?? { name: e.displayName, hours: 0, open: 0 }
      if (e.clockOutAt) t.hours += e.hours ?? 0
      else t.open += 1
      m.set(e.userId, t)
    }
    return [...m.values()].sort((a, b) => a.name.localeCompare(b.name))
  }, [entries])
  const grand = totals.reduce((s, t) => s + t.hours, 0)

  return (
    <>
      <div className="toolbar" role="group" aria-label="Date range">
        <div className="tabs">
          {PRESETS.map((p) => (
            <button
              key={p.id}
              type="button"
              className={preset === p.id ? 'tab tab-on' : 'tab'}
              aria-pressed={preset === p.id}
              onClick={() => setPreset(p.id)}
            >
              {p.label}
            </button>
          ))}
        </div>
      </div>
      <div className="filters">
        {preset === 'custom' ? (
          <>
            <Field label="From">
              <input type="date" value={customFrom} max={customTo} onChange={(e) => setCustomFrom(e.target.value)} />
            </Field>
            <Field label="To">
              <input type="date" value={customTo} min={customFrom} onChange={(e) => setCustomTo(e.target.value)} />
            </Field>
          </>
        ) : (
          <p className="muted range-label">{fromDate === toDate ? fromDate : `${fromDate} to ${toDate}`}</p>
        )}
        {manager && (
          <Field label="Staff">
            <select value={userId} onChange={(e) => setUserId(e.target.value)}>
              <option value="">Everyone</option>
              {(staff.data ?? []).map((s) => (
                <option key={s.userId} value={s.userId}>
                  {s.displayName}
                </option>
              ))}
            </select>
          </Field>
        )}
      </div>
      {!validRange && (
        <div className="alert alert-error" role="alert">
          Choose a valid date range (From must not be after To).
        </div>
      )}
      {validRange && q.isLoading && <Spinner label="Loading records…" />}
      {q.error && <ErrorBox error={q.error} onRetry={() => void q.refetch()} />}

      {validRange && q.data && (
        <>
          <section className="card totals" aria-label="Totals">
            <h2>Totals</h2>
            {totals.length === 0 ? (
              <p className="muted">No time entries in this range.</p>
            ) : (
              <ul className="row-list">
                {totals.map((t) => (
                  <li key={t.name} className="totals-row">
                    <strong>{t.name}</strong>
                    <span>
                      {hoursText(t.hours)}
                      {t.open > 0 && <span className="badge badge-open totals-open">{t.open} in progress</span>}
                    </span>
                  </li>
                ))}
                <li className="totals-row totals-grand">
                  <strong>Total</strong>
                  <strong>{hoursText(grand)}</strong>
                </li>
              </ul>
            )}
            <small className="muted">Shifts still open are not counted.</small>
          </section>

          {entries.length > 0 && (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Date</th>
                    <th scope="col">Name</th>
                    <th scope="col">In</th>
                    <th scope="col">Out</th>
                    <th scope="col" className="num">
                      Hours
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {entries.map((e) => (
                    <tr key={e.id}>
                      <td>{formatDate(e.clockInAt, tz)}</td>
                      <td>{e.displayName}</td>
                      <td>{formatTime(e.clockInAt, tz)}</td>
                      <td>
                        {e.clockOutAt ? formatTime(e.clockOutAt, tz) : <span className="badge badge-open">Open</span>}
                      </td>
                      <td className="num">{e.hours === null ? '—' : e.hours.toFixed(2)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </>
  )
}

export function TimeRecordsPage() {
  const hasLocation = useAuthStore((s) => !!s.user?.location)
  const loc = useCurrentLocation()
  return (
    <section className="page">
      <PageHeader title="Time records">
        <Link className="btn" to="/timeclock">
          Back to time clock
        </Link>
      </PageHeader>
      {!hasLocation ? (
        <p className="muted">
          Time records are per location. Pick a location in the header (you are in the All locations view).
        </p>
      ) : loc.isLoading ? (
        <Spinner />
      ) : (
        <RecordsView key={loc.data?.timeZone} tz={loc.data?.timeZone} />
      )}
    </section>
  )
}
