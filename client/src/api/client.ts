import { strings } from '@/lib/i18n'
const KEY_STORAGE = 'bt-device-key'
export const ACCESS_LOST_EVENT = 'bt-access-lost'
const INVITE_STORAGE = 'bt-invite'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

function read(name: string): string | null {
  try {
    return localStorage.getItem(name)
  } catch {
    return null
  }
}

function write(name: string, value: string | null) {
  try {
    if (value) localStorage.setItem(name, value)
    else localStorage.removeItem(name)
  } catch {
    return
  }
}

export function rememberInviteFromUrl() {
  const invite = new URLSearchParams(location.search).get('invite')
  if (invite) write(INVITE_STORAGE, invite)
}

let registering: Promise<void> | null = null

export const hasDevice = () => read(KEY_STORAGE) !== null

export const isInviteRequired = (error: unknown) => error instanceof ApiError && error.status === 403

export const isInvalidLinkCode = (error: unknown) => error instanceof ApiError && error.status === 400

export async function registerWithLinkCode(code: string) {
  await register(null, code)
}

export async function registerWithInvite(invite: string) {
  await register(invite.trim())
  write(INVITE_STORAGE, invite.trim())
}

const deviceName = () => (navigator.userAgent.includes('Mobile') ? strings().common.phoneBrowser : strings().common.browser)

function registrationFailed(status: number, detail: string | undefined): ApiError {
  if (status === 403) window.dispatchEvent(new Event(ACCESS_LOST_EVENT))
  return new ApiError(status, detail ?? strings().common.inviteNeeded)
}

async function register(invite = read(INVITE_STORAGE), linkCode: string | null = null) {
  const response = await fetch('/api/devices', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ platform: 'web', name: deviceName(), invite, linkCode }),
  })
  const body = await response.json().catch(() => null)
  if (!response.ok) throw registrationFailed(response.status, body?.detail)
  write(KEY_STORAGE, body.key)
}

export async function ensureRegistered() {
  await ensureDevice()
}

function ensureDevice(): Promise<void> {
  if (read(KEY_STORAGE)) return Promise.resolve()
  registering ??= register().finally(() => {
    registering = null
  })
  return registering
}

async function parse<T>(response: Response): Promise<T> {
  if (response.status === 204) return undefined as T
  const body = await response.json().catch(() => null)
  if (!response.ok) throw new ApiError(response.status, body?.detail ?? strings().common.requestFailed(response.status))
  return body as T
}

export async function api<T>(path: string, init: RequestInit = {}, retried = false): Promise<T> {
  await ensureDevice()
  const headers = new Headers(init.headers)
  headers.set('Authorization', `Bearer ${read(KEY_STORAGE)}`)
  if (init.body) headers.set('Content-Type', 'application/json')

  const response = await fetch(path, { ...init, headers })
  if (response.status === 401 && !retried) {
    write(KEY_STORAGE, null)
    return api<T>(path, init, true)
  }
  return parse<T>(response)
}
