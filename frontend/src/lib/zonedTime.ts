/** Wall-clock helpers for a given IANA time zone (falls back to the browser zone). Dates are 'YYYY-MM-DD' strings. */
const browserZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone

function safeZone(tz?: string) {
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: tz })
    return tz || browserZone()
  } catch {
    return browserZone()
  }
}

/** Offset (ms) of `tz` from UTC at the instant `ts`. */
function offsetAt(ts: number, tz: string) {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: tz,
    hourCycle: 'h23',
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: 'numeric',
    minute: 'numeric',
    second: 'numeric',
  }).formatToParts(new Date(ts))
  const g = (t: string) => Number(parts.find((p) => p.type === t)?.value)
  return (
    Date.UTC(g('year'), g('month') - 1, g('day'), g('hour'), g('minute'), g('second')) - Math.floor(ts / 1000) * 1000
  )
}

/** Today's date in the zone. */
export function todayIn(tz?: string, now = new Date()) {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: safeZone(tz),
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(now)
}

export function addDays(date: string, n: number) {
  const [y, m, d] = date.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d + n)).toISOString().slice(0, 10)
}

/** 0 = Monday ... 6 = Sunday */
export const weekdayIndex = (date: string) => (new Date(`${date}T00:00:00Z`).getUTCDay() + 6) % 7

/** UTC ISO instant of local midnight at the start of `date` in the zone. */
export function startOfDayUtc(date: string, tz?: string) {
  const zone = safeZone(tz)
  const [y, m, d] = date.split('-').map(Number)
  const guess = Date.UTC(y, m - 1, d)
  let ts = guess - offsetAt(guess, zone)
  ts = guess - offsetAt(ts, zone) // re-check across a DST change
  return new Date(ts).toISOString()
}
