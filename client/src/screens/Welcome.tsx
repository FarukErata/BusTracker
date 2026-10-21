import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, Outlet } from 'react-router'
import { ACCESS_LOST_EVENT, ensureRegistered, hasDevice, isInvalidLinkCode, isInviteRequired, registerWithInvite, registerWithLinkCode } from '@/api/client'
import { ErrorState } from '@/components/bits'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { isCompleteLinkCode, linkCodeInput } from '@/lib/format'
import { strings } from '@/lib/i18n'

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

const joinModes = (t = strings()): Record<JoinMode, JoinModeSettings> => ({
  invite: {
    lead: t.welcome.inviteLead,
    placeholder: t.welcome.inviteCode,
    label: t.welcome.inviteCode,
    action: t.welcome.join,
    busy: t.welcome.joining,
    wrong: t.welcome.inviteWrong,
    other: t.welcome.haveLink,
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
    lead: t.welcome.linkLead,
    placeholder: 'ABC-123',
    label: t.devices.linkCode,
    action: t.welcome.link,
    busy: t.devices.linking,
    wrong: t.devices.codeWrong,
    other: t.welcome.haveInvite,
    next: 'invite',
    inputClass: 'h-10 text-center font-mono tracking-[0.15em]',
    autoCapitalize: 'characters',
    format: linkCodeInput,
    isComplete: isCompleteLinkCode,
    autoSubmits: true,
    join: registerWithLinkCode,
    isWrong: isInvalidLinkCode,
  },
})

function InviteScreen({ onJoined }: { onJoined: () => void }) {
  const [mode, setMode] = useState<JoinMode>('invite')
  const [code, setCode] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const autoTried = useRef(false)
  const settings = joinModes()[mode]

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
          {strings().welcome.androidApp}
        </Link>
      </div>
    </div>
  )
}
