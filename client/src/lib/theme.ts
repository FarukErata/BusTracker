import { useCallback, useSyncExternalStore } from 'react'

export type Theme = 'light' | 'dark'

const STORAGE = 'bt-theme'
const listeners = new Set<() => void>()
const media = window.matchMedia('(prefers-color-scheme: dark)')

function stored(): Theme | null {
  try {
    const value = localStorage.getItem(STORAGE)
    return value === 'light' || value === 'dark' ? value : null
  } catch {
    return null
  }
}

function current(): Theme {
  return stored() ?? (media.matches ? 'dark' : 'light')
}

export function applyTheme() {
  document.documentElement.classList.toggle('dark', current() === 'dark')
  listeners.forEach(listener => listener())
}

media.addEventListener('change', applyTheme)

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useTheme() {
  const theme = useSyncExternalStore(subscribe, current)
  const toggle = useCallback(() => {
    try {
      localStorage.setItem(STORAGE, current() === 'dark' ? 'light' : 'dark')
    } catch {
      return
    }
    applyTheme()
  }, [])
  return { theme, toggle }
}
