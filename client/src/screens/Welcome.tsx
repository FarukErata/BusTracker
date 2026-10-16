import { useEffect, useState, type FormEvent } from 'react'
import { Link, Outlet } from 'react-router'
import { ensureRegistered, hasDevice, isInviteRequired, registerWithInvite } from '@/api/client'
import { ErrorState } from '@/components/bits'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

type Access = 'checking' | 'ready' | 'invite' | 'error'

export function AccessGate() {
  const [access, setAccess] = useState<Access>(() => (hasDevice() ? 'ready' : 'checking'))
  const [message, setMessage] = useState('')

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

function InviteScreen({ onJoined }: { onJoined: () => void }) {
  const [code, setCode] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function join(event: FormEvent) {
    event.preventDefault()
    if (!code.trim()) return
    setPending(true)
    setError(null)
    try {
      await registerWithInvite(code)
      onJoined()
    } catch (failure) {
      setError(isInviteRequired(failure) ? 'That code doesn’t work. Check it and try again.' : (failure as Error).message)
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-sm flex-col justify-center gap-6 px-6">
      <div className="flex flex-col gap-2 text-center">
        <h1 className="text-2xl font-semibold tracking-tight">Kaç Kaldı</h1>
        <p className="text-sm text-muted-foreground">This server is private. Enter the invite code you were given.</p>
      </div>
      <form onSubmit={join} className="flex flex-col gap-3">
        <Input
          value={code}
          onChange={event => {
            setCode(event.target.value)
            setError(null)
          }}
          placeholder="Invite code"
          aria-label="Invite code"
          aria-invalid={error !== null}
          autoFocus
          autoComplete="off"
          autoCapitalize="off"
          spellCheck={false}
          className="h-10 text-center"
        />
        {error && (
          <p role="alert" className="text-center text-sm text-destructive">
            {error}
          </p>
        )}
        <Button type="submit" size="lg" disabled={pending || !code.trim()} className="pressable">
          {pending ? 'Joining…' : 'Join'}
        </Button>
      </form>
      <Link to="/android" className="text-center text-sm text-muted-foreground underline underline-offset-4 hover:text-foreground">
        Get the Android app
      </Link>
    </div>
  )
}
