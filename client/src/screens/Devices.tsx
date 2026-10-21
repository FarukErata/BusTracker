import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Check, Copy, Monitor, Smartphone } from 'lucide-react'
import { isInvalidLinkCode, registerWithLinkCode } from '@/api/client'
import { useCreateLinkCode, useMe, useRemoveDevice } from '@/api/hooks'
import type { DeviceView } from '@/api/types'
import { BackButton, ErrorState, RowsSkeleton, SectionTitle } from '@/components/bits'
import { Badge } from '@/components/reui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemMedia, ItemTitle } from '@/components/ui/item'
import { ago, countdown, isCompleteLinkCode, linkCodeInput } from '@/lib/format'
import { strings, useLang } from '@/lib/i18n'
import { useSecondsUntil } from '@/lib/useSecondsUntil'

export function Devices() {
  useLang()
  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-md flex-col pb-8">
      <header className="flex items-center gap-2 px-3 pt-3">
        <BackButton />
        <h1 className="flex-1 text-lg font-semibold tracking-tight">{strings().devices.title}</h1>
      </header>
      <SectionTitle>{strings().devices.add}</SectionTitle>
      <LinkCodeCard />
      <SectionTitle>{strings().devices.yours}</SectionTitle>
      <DeviceList />
      <JoinAnotherDevice />
    </div>
  )
}

function LinkCodeCard() {
  const create = useCreateLinkCode()
  const code = create.data
  const remaining = useSecondsUntil(code?.expiresAt)

  if (code && remaining > 0) return <LinkCodeShown code={code.code} remaining={remaining} />
  return <LinkCodeRequest expired={code !== undefined} pending={create.isPending} error={create.error} onRequest={() => create.mutate()} />
}

function LinkCodeRequest({ expired, pending, error, onRequest }: { expired: boolean; pending: boolean; error: Error | null; onRequest: () => void }) {
  const t = strings().devices
  const label = pending ? t.getting : expired ? t.getNew : t.get
  return (
    <div className="flex flex-col items-start gap-3 px-3">
      <p className="text-sm text-muted-foreground">
        {expired ? t.expiredText : t.getText}
      </p>
      <Button onClick={onRequest} disabled={pending} className="pressable">
        {label}
      </Button>
      {error && (
        <p role="alert" className="text-sm text-destructive">
          {error.message}
        </p>
      )}
    </div>
  )
}

function LinkCodeShown({ code, remaining }: { code: string; remaining: number }) {
  return (
    <div className="mx-3 flex flex-col items-center gap-3 rounded-xl border p-5 text-center">
      <p className="font-mono text-4xl font-semibold tracking-[0.15em] tabular-nums" aria-label={strings().devices.codeLabel(code.split('').join(' '))}>
        {code}
      </p>
      <p className="text-sm text-muted-foreground tabular-nums">{strings().devices.expiresIn(countdown(remaining))}</p>
      <p className="text-sm text-muted-foreground">
        {strings().devices.otherDeviceBefore}<span className="text-foreground">{strings().devices.otherDeviceAction}</span>{strings().devices.otherDeviceAfter}
      </p>
      <CopyButton text={code} />
    </div>
  )
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false)
  useEffect(() => {
    if (!copied) return
    const timer = setTimeout(() => setCopied(false), 1500)
    return () => clearTimeout(timer)
  }, [copied])

  return (
    <Button
      variant="outline"
      size="sm"
      className="pressable"
      onClick={() => navigator.clipboard?.writeText(text).then(() => setCopied(true), () => setCopied(false))}
    >
      {copied ? <Check /> : <Copy />}
      {copied ? strings().common.copied : strings().common.copy}
    </Button>
  )
}

function DeviceList() {
  const me = useMe()
  const remove = useRemoveDevice()
  const [confirming, setConfirming] = useState<number | null>(null)
  const [now] = useState(() => Date.now())

  if (me.isPending) return <RowsSkeleton rows={2} />
  if (me.isError) return <ErrorState message={me.error.message} onRetry={() => me.refetch()} />

  return (
    <ItemGroup>
      {me.data.devices.map(device => (
        <DeviceRow
          key={device.id}
          device={device}
          now={now}
          confirming={confirming === device.id}
          removing={remove.isPending}
          onAsk={() => setConfirming(device.id)}
          onCancel={() => setConfirming(null)}
          onRemove={() => remove.mutate(device.id, { onSettled: () => setConfirming(null) })}
        />
      ))}
    </ItemGroup>
  )
}

interface DeviceRowProps {
  device: DeviceView
  now: number
  confirming: boolean
  removing: boolean
  onAsk: () => void
  onCancel: () => void
  onRemove: () => void
}

function DeviceRow({ device, now, ...actions }: DeviceRowProps) {
  const Icon = isPhone(device) ? Smartphone : Monitor
  return (
    <Item>
      <ItemMedia variant="icon"><Icon className="text-muted-foreground" /></ItemMedia>
      <ItemContent>
        <ItemTitle>
          {device.name}
          {device.current && (
            <Badge variant="primary-light" size="sm">
              {strings().devices.thisDevice}
            </Badge>
          )}
        </ItemTitle>
        <ItemDescription>{lastUsed(device, now)}</ItemDescription>
      </ItemContent>
      {!device.current && <ItemActions><RemoveControls {...actions} /></ItemActions>}
    </Item>
  )
}

function RemoveControls({ confirming, removing, onAsk, onCancel, onRemove }: Omit<DeviceRowProps, 'device' | 'now'>) {
  if (!confirming)
    return (
      <Button variant="ghost" size="sm" onClick={onAsk}>
        {strings().common.remove}
      </Button>
    )
  return (
    <>
      <Button variant="ghost" size="sm" onClick={onCancel}>
        {strings().common.cancel}
      </Button>
      <Button variant="destructive" size="sm" className="pressable" disabled={removing} onClick={onRemove}>
        {strings().common.remove}
      </Button>
    </>
  )
}

const lastUsed = (device: DeviceView, now: number) =>
  device.current ? strings().devices.inUse : strings().devices.lastUsed(ago((now - Date.parse(device.lastSeenAt)) / 1000))

const isPhone = (device: DeviceView) => device.platform === 'android' || device.platform === 'ios' || /phone/i.test(device.name)

function JoinAnotherDevice() {
  const [open, setOpen] = useState(false)
  const [code, setCode] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const autoTried = useRef(false)

  function change(value: string) {
    const next = linkCodeInput(value)
    setCode(next)
    setError(null)
    if (isCompleteLinkCode(next) && !autoTried.current) {
      autoTried.current = true
      void submit(next)
    }
  }

  function join(event: FormEvent) {
    event.preventDefault()
    if (isCompleteLinkCode(code) && !pending) void submit(code)
  }

  async function submit(value: string) {
    setPending(true)
    setError(null)
    try {
      await registerWithLinkCode(value)
      location.assign('/')
    } catch (failure) {
      setError(isInvalidLinkCode(failure) ? strings().devices.codeWrong : (failure as Error).message)
      setPending(false)
    }
  }

  if (!open)
    return (
      <button type="button" onClick={() => setOpen(true)} className="mt-6 px-3 text-left text-sm text-muted-foreground underline underline-offset-4 hover:text-foreground">
        {strings().devices.useCode}
      </button>
    )

  return (
    <form onSubmit={join} className="mx-3 mt-6 flex flex-col gap-3 rounded-xl border p-4">
      <p className="text-sm text-muted-foreground">
        {strings().devices.switchWarning}
      </p>
      <Input
        value={code}
        onChange={event => change(event.target.value)}
        placeholder="ABC-123"
        aria-label={strings().devices.linkCode}
        aria-invalid={error !== null}
        autoFocus
        autoComplete="off"
        autoCapitalize="characters"
        spellCheck={false}
        className="h-10 text-center font-mono tracking-[0.15em]"
      />
      {error && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
      <div className="flex gap-2">
        <Button type="button" variant="ghost" onClick={() => setOpen(false)}>
          {strings().common.cancel}
        </Button>
        <Button type="submit" disabled={pending || !isCompleteLinkCode(code)} className="pressable flex-1">
          {pending ? strings().devices.linking : strings().devices.linkBrowser}
        </Button>
      </div>
    </form>
  )
}
