import { useSyncExternalStore } from 'react'
import { en, type Strings } from './en'
import { tr } from './tr'

export type Lang = 'tr' | 'en'

export const LANGS: Lang[] = ['tr', 'en']

const KEY = 'lang'
const DICTIONARIES: Record<Lang, Strings> = { en, tr }
const listeners = new Set<() => void>()
let current: Lang = initial()

function initial(): Lang {
  try {
    const saved = localStorage.getItem(KEY)
    if (saved === 'tr' || saved === 'en') return saved
  } catch {
    return deviceLang()
  }
  return deviceLang()
}

function deviceLang(): Lang {
  return navigator.language.toLowerCase().startsWith('tr') ? 'tr' : 'en'
}

function apply(lang: Lang) {
  document.documentElement.lang = lang
}

export function setLang(lang: Lang) {
  current = lang
  apply(lang)
  try {
    localStorage.setItem(KEY, lang)
  } catch {
    return
  } finally {
    listeners.forEach(l => l())
  }
}

export function strings(): Strings {
  return DICTIONARIES[current]
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useLang(): Lang {
  return useSyncExternalStore(subscribe, () => current, () => current)
}

export function useT(): Strings {
  return DICTIONARIES[useLang()]
}

apply(current)
