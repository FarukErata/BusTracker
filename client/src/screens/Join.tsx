import { useParams, useSearchParams } from 'react-router'
import { Download, Smartphone } from 'lucide-react'
import { buttonVariants } from '@/components/ui/button'
import { APK_URL, isAndroid, openAppUrl } from '@/lib/androidApp'
import { countdown, linkCodeInput } from '@/lib/format'
import { strings, useLang } from '@/lib/i18n'
import { useSecondsUntil } from '@/lib/useSecondsUntil'
import { cn } from '@/lib/utils'

export function Join() {
  useLang()
  const { code = '' } = useParams()
  const [params] = useSearchParams()
  const expiresAt = Number(params.get('e')) || undefined
  const remaining = useSecondsUntil(expiresAt)
  const expired = expiresAt !== undefined && remaining <= 0
  const shown = linkCodeInput(code)

  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-md flex-col items-center gap-5 px-6 pt-12 pb-10 text-center">
      <h1 className="text-xl font-semibold tracking-tight">{strings().join.title}</h1>
      {expired ? <Expired /> : isAndroid() ? <Steps code={code} /> : <NotAndroid />}
      {!expired && (
        <div className="flex flex-col items-center gap-1 pt-2">
          <p className="text-sm text-muted-foreground">{strings().join.orType}</p>
          <p className="font-mono text-3xl font-semibold tracking-[0.15em] tabular-nums">{shown}</p>
          {expiresAt !== undefined && <p className="text-sm text-muted-foreground tabular-nums">{strings().join.worksOnce(countdown(remaining))}</p>}
        </div>
      )}
    </div>
  )
}

function Steps({ code }: { code: string }) {
  return (
    <ol className="flex w-full flex-col gap-4 text-left">
      <li className="flex flex-col gap-2 rounded-xl border p-4">
        <span className="text-sm font-medium">{strings().join.install}</span>
        <span className="text-sm text-muted-foreground">{strings().join.installText}</span>
        <a href={APK_URL} className={cn(buttonVariants({ size: 'lg' }), 'pressable gap-2')}>
          <Download />
          {strings().join.download}
        </a>
      </li>
      <li className="flex flex-col gap-2 rounded-xl border p-4">
        <span className="text-sm font-medium">{strings().join.open}</span>
        <span className="text-sm text-muted-foreground">{strings().join.openText}</span>
        <a href={openAppUrl(code)} className={cn(buttonVariants({ size: 'lg', variant: 'outline' }), 'pressable gap-2')}>
          <Smartphone />
          {strings().join.openApp}
        </a>
      </li>
    </ol>
  )
}

function NotAndroid() {
  return <p className="max-w-xs text-sm text-muted-foreground">{strings().join.notAndroid}</p>
}

function Expired() {
  return (
    <p className="max-w-xs text-sm text-muted-foreground">
      {strings().join.expired}
    </p>
  )
}
