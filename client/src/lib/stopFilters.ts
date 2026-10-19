import { useState } from 'react'

const key = (stopCode: string) => `stop-lines:${stopCode}`

function read(stopCode: string): string[] {
  try {
    const value = JSON.parse(localStorage.getItem(key(stopCode)) ?? '[]')
    return Array.isArray(value) ? value.filter((v): v is string => typeof v === 'string') : []
  } catch {
    return []
  }
}

function write(stopCode: string, lines: string[]) {
  try {
    if (lines.length === 0) localStorage.removeItem(key(stopCode))
    else localStorage.setItem(key(stopCode), JSON.stringify(lines))
  } catch {
    return
  }
}

export function useStopLineFilter(stopCode: string) {
  const [saved, setSaved] = useState(() => ({ stopCode, lines: read(stopCode) }))
  const lines = saved.stopCode === stopCode ? saved.lines : read(stopCode)
  const save = (next: string[]) => {
    write(stopCode, next)
    setSaved({ stopCode, lines: next })
  }
  return [lines, save] as const
}
