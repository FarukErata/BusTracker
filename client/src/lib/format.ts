const TR = 'tr-TR'

export function titleCase(text: string | null | undefined): string {
  if (!text) return ''
  return text
    .trim()
    .toLocaleLowerCase(TR)
    .replace(/(^|[\s(\-/.])(\p{L})/gu, (_, before: string, letter: string) => before + letter.toLocaleUpperCase(TR))
}

export function minutes(etaSeconds: number): string {
  return etaSeconds < 60 ? '<1' : String(Math.round(etaSeconds / 60))
}

export function distance(meters: number): string {
  return meters < 1000 ? `${Math.round(meters / 10) * 10} m` : `${(meters / 1000).toFixed(1)} km`
}

export function ago(seconds: number): string {
  if (seconds < 10) return 'just now'
  if (seconds < 60) return `${Math.round(seconds)} s ago`
  if (seconds < 3600) return `${Math.round(seconds / 60)} min ago`
  if (seconds < 86400) return `${Math.round(seconds / 3600)} h ago`
  const days = Math.round(seconds / 86400)
  return days === 1 ? 'yesterday' : `${days} days ago`
}

export function linkCodeInput(text: string): string {
  const compact = text.toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 6)
  return compact.length > 3 ? `${compact.slice(0, 3)}-${compact.slice(3)}` : compact
}

export const isCompleteLinkCode = (code: string) => /^[A-Z0-9]{3}-[A-Z0-9]{3}$/.test(code)

export function countdown(seconds: number): string {
  const s = Math.max(0, Math.ceil(seconds))
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
}

export function stopsAway(count: number): string {
  if (count === 0) return 'At your stop'
  return `${count} ${count === 1 ? 'stop' : 'stops'} away`
}

export function busWhere(notDeparted: boolean, stops: number): string {
  return notDeparted ? 'At the first stop' : stopsAway(stops)
}

export function directionLabel(from: string, to: string): string {
  return `${titleCase(from)} → ${titleCase(to)}`
}

export type ReportAge = 'fresh' | 'old' | 'stale'

const OLD_REPORT_SECONDS = 90
const STALE_REPORT_SECONDS = 180

export function reportAge(seconds: number): ReportAge {
  if (seconds >= STALE_REPORT_SECONDS) return 'stale'
  return seconds >= OLD_REPORT_SECONDS ? 'old' : 'fresh'
}

export const secondsSince = (iso: string, now = Date.now()) => Math.max(0, (now - Date.parse(iso)) / 1000)

export const seenAgo = (seconds: number) => `seen ${ago(seconds)}`
