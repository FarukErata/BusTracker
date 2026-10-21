import { useSyncExternalStore } from 'react'

export type Recent =
  | { kind: 'stop'; code: string; name: string; district?: string }
  | { kind: 'line'; code: string; direction: string; title: string }

const KEY = 'recents'
const MAX = 5
const listeners = new Set<() => void>()
let cache: Recent[] | null = null
const EMPTY: Recent[] = []

function read(): Recent[] {
  if (cache) return cache
  try {
    const value = JSON.parse(localStorage.getItem(KEY) ?? '[]')
    cache = Array.isArray(value) ? (value as Recent[]) : []
  } catch {
    cache = []
  }
  return cache
}

function write(next: Recent[]) {
  cache = next
  try {
    if (next.length === 0) localStorage.removeItem(KEY)
    else localStorage.setItem(KEY, JSON.stringify(next))
  } catch {
    return
  } finally {
    listeners.forEach(l => l())
  }
}

const sameItem = (a: Recent, b: Recent) =>
  a.kind === b.kind && a.code === b.code && (a.kind === 'stop' || b.kind === 'stop' || a.direction === b.direction)

export function rememberRecent(item: Recent) {
  const current = read()
  if (current[0] && sameItem(current[0], item) && JSON.stringify(current[0]) === JSON.stringify(item)) return
  write([item, ...current.filter(r => !sameItem(r, item))].slice(0, MAX))
}

export function clearRecents() {
  write([])
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useRecents() {
  return useSyncExternalStore(subscribe, read, () => EMPTY)
}
