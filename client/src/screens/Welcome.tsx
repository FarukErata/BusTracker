import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, Outlet } from 'react-router'
import { ACCESS_LOST_EVENT, ensureRegistered, hasDevice, isInvalidLinkCode, isInviteRequired, registerWithInvite, registerWithLinkCode } from '@/api/client'
import { ErrorState } from '@/components/bits'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { isCompleteLinkCode, linkCodeInput } from '@/lib/format'

type Access = 'checking' | 'ready' | 'invite' | 'error'

export function AccessGate() {
  const [access, setAccess] = useState<Access>(() => (hasDevice() ? 'ready' : 'checking'))
  const [message, setMessage] = useState('')

  useEffect(() => {
    const lost = () => setAccess('invite')
    window.addEventListener(ACCESS_LOST_EVENT, lost)
    return () => window.removeEventListener(ACCESS_LOST_EVENT, lost)
  }, [])

  useEffect(() => {
    if (access !== 'checking') return
    ensureRegistered()
      .then(() => setAccess('ready'))
      .catch((error: Error) => {
        setMessage(error.message)
        setAccess(isInviteRequired(error) ? 'invite' : 'error')
      })
  }, [access])

  if (access === 'ready') return <Outlet />
  if (access === 'invite') return <InviteScreen onJoined={() => setAccess('ready')} />
  if (access === 'error')
    return (
      <div className="mx-auto flex min-h-dvh w-full max-w-md flex-col justify-center">
        <ErrorState message={message} onRetry={() => setAccess('checking')} />
      </div>
    )
  return <div className="min-h-dvh" />
}

type JoinMode = 'invite' | 'link'

interface JoinModeSettings {
  lead: string
  placeholder: string
  label: string
  action: string
  busy: string
  wrong: string
  other: string
  next: JoinMode
  inputClass: string
  autoCapitalize: 'off' | 'characters'
  format: (value: string) => string
  isComplete: (value: string) => boolean
  autoSubmits: boolean
  join: (value: string) => Promise<void>
  isWrong: (error: unknown) => boolean
}

const JOIN_MODES: Record<JoinMode, JoinModeSettings> = {
  invite: {
    lead: 'This server is private. Enter the invite code you were given.',
    placeholder: 'Invite code',
    label: 'Invite code',
    action: 'Join',
    busy: 'Joining…',
    wrong: 'That code doesn’t work. Check it and try again.',
    other: 'Already using Kaç Kaldı on another device? Enter a link code',
    next: 'link',
    inputClass: 'h-10 text-center',
    autoCapitalize: 'off',
    format: value => value,
    isComplete: value => value.trim().length > 0,
    autoSubmits: false,
    join: registerWithInvite,
    isWrong: isInviteRequired,
  },
  link: {
    lead: 'On your other device, open Devices and tap Get a link code. Enter the code here.',
    placeholder: 'ABC-123',
    label: 'Link code',
    action: 'Link',
    busy: 'Linking…',
    wrong: 'That code doesn’t work or has expired. Get a new one on the other device.',
    other: 'I have an invite code instead',
    next: 'invite',
    inputClass: 'h-10 text-center font-mono tracking-[0.15em]',
    autoCapitalize: 'characters',
    format: linkCodeInput,
    isComplete: isCompleteLinkCode,
    autoSubmits: true,
    join: registerWithLinkCode,
    isWrong: isInvalidLinkCode,
  },
}

function InviteScreen({ onJoined }: { onJoined: () => void }) {
  const [mode, setMode] = useState<JoinMode>('invite')
  const [code, setCode] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const autoTried = useRef(false)
  const settings = JOIN_MODES[mode]

  function switchMode() {
    setMode(settings.next)
    setCode('')
    setError(null)
  }

  function change(value: string) {
    const next = settings.format(value)
    setCode(next)
    setError(null)
    if (!settings.autoSubmits || autoTried.current || !settings.isComplete(next)) return
    autoTried.current = true
    void submit(next)
  }

  function join(event: FormEvent) {
    event.preventDefault()
    if (settings.isComplete(code) && !pending) void submit(code)
  }

  async function submit(value: string) {
    setPending(true)
    setError(null)
    try {
      await settings.join(value)
      onJoined()
    } catch (failure) {
      setError(settings.isWrong(failure) ? settings.wrong : (failure as Error).message)
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-sm flex-col justify-center gap-6 px-6">
      <div className="flex flex-col gap-2 text-center">
        <h1 className="text-2xl font-semibold tracking-tight">Kaç Kaldı</h1>
        <p className="text-sm text-muted-foreground">{settings.lead}</p>
      </div>
      <form onSubmit={join} className="flex flex-col gap-3">
        <Input
          key={mode}
          value={code}
          onChange={event => change(event.target.value)}
          placeholder={settings.placeholder}
          aria-label={settings.label}
          aria-invalid={error !== null}
          autoFocus
          autoComplete="off"
          autoCapitalize={settings.autoCapitalize}
          spellCheck={false}
          className={settings.inputClass}
        />
        {error && (
          <p role="alert" className="text-center text-sm text-destructive">
            {error}
          </p>
        )}
        <Button type="submit" size="lg" disabled={pending || !settings.isComplete(code)} className="pressable">
          {pending ? settings.busy : settings.action}
        </Button>
      </form>
      <div className="flex flex-col items-center gap-3 text-sm">
        <button type="button" onClick={switchMode} className="text-muted-foreground underline underline-offset-4 hover:text-foreground">
          {settings.other}
        </button>
        <Link to="/android" className="text-muted-foreground underline underline-offset-4 hover:text-foreground">
          Get the Android app
        </Link>
      </div>
    </div>
  )
}
