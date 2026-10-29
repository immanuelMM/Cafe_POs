const peso = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' })
export const formatMoney = (n: number | null | undefined) => peso.format(n ?? 0)

const qty = new Intl.NumberFormat('en-PH', { maximumFractionDigits: 3 })
export const formatQty = (n: number) => qty.format(n)

export function formatDateTime(iso: string | null | undefined, timeZone = 'Asia/Manila') {
  if (!iso) return ''
  return new Intl.DateTimeFormat('en-PH', { dateStyle: 'medium', timeStyle: 'short', timeZone }).format(new Date(iso))
}

/** Round to 2dp, half away from zero, matching the backend. */
export const round2 = (n: number) => (Math.sign(n) * Math.round(Math.abs(n) * 100 + Number.EPSILON)) / 100

export function formatTime(iso: string | null | undefined, timeZone?: string) {
  if (!iso) return ''
  return new Intl.DateTimeFormat('en-PH', { hour: 'numeric', minute: '2-digit', timeZone }).format(new Date(iso))
}

export function formatDate(iso: string | null | undefined, timeZone?: string) {
  if (!iso) return ''
  return new Intl.DateTimeFormat('en-PH', {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone,
  }).format(new Date(iso))
}
