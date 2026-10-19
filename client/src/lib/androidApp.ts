export const APK_URL = 'https://github.com/FarukErata/BusTracker/releases/latest/download/kackaldi.apk'

const PACKAGE = 'com.farukerata.kackaldi'

export const isAndroid = () => /android/i.test(navigator.userAgent)

const compact = (code: string) => code.replace(/[^A-Za-z0-9]/g, '').toUpperCase()

export function joinUrl(code: string, expiresAt: string) {
  return `${location.origin}/join/${compact(code)}?e=${Date.parse(expiresAt)}`
}

export function openAppUrl(code: string) {
  return `intent://join/${compact(code)}#Intent;scheme=kackaldi;package=${PACKAGE};S.browser_fallback_url=${encodeURIComponent(APK_URL)};end`
}
