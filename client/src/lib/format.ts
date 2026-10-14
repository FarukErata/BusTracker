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
  return `${Math.round(seconds / 60)} min ago`
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
