import { useParams, useSearchParams } from 'react-router'
import { Download, Smartphone } from 'lucide-react'
import { buttonVariants } from '@/components/ui/button'
import { APK_URL, isAndroid, openAppUrl } from '@/lib/androidApp'
import { countdown, linkCodeInput } from '@/lib/format'
import { useSecondsUntil } from '@/lib/useSecondsUntil'
import { cn } from '@/lib/utils'

export function Join() {
  const { code = '' } = useParams()
  const [params] = useSearchParams()
  const expiresAt = Number(params.get('e')) || undefined
  const remaining = useSecondsUntil(expiresAt)
  const expired = expiresAt !== undefined && remaining <= 0
  const shown = linkCodeInput(code)

  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-md flex-col items-center gap-5 px-6 pt-12 pb-10 text-center">
      <h1 className="text-xl font-semibold tracking-tight">Kaç Kaldı on this phone</h1>
      {expired ? <Expired /> : isAndroid() ? <Steps code={code} /> : <NotAndroid />}
      {!expired && (
        <div className="flex flex-col items-center gap-1 pt-2">
          <p className="text-sm text-muted-foreground">Or type this link code in the app</p>
          <p className="font-mono text-3xl font-semibold tracking-[0.15em] tabular-nums">{shown}</p>
          {expiresAt !== undefined && <p className="text-sm text-muted-foreground tabular-nums">Works once, for {countdown(remaining)}</p>}
        </div>
      )}
    </div>
  )
}

function Steps({ code }: { code: string }) {
  return (
    <ol className="flex w-full flex-col gap-4 text-left">
      <li className="flex flex-col gap-2 rounded-xl border p-4">
        <span className="text-sm font-medium">1. Install the app</span>
        <span className="text-sm text-muted-foreground">Skip this if it is already installed. Android asks once to allow installs from your browser.</span>
        <a href={APK_URL} className={cn(buttonVariants({ size: 'lg' }), 'pressable gap-2')}>
          <Download />
          Download Kaç Kaldı
        </a>
      </li>
      <li className="flex flex-col gap-2 rounded-xl border p-4">
        <span className="text-sm font-medium">2. Open it signed in</span>
        <span className="text-sm text-muted-foreground">The app opens linked to your account, with your saved stops.</span>
        <a href={openAppUrl(code)} className={cn(buttonVariants({ size: 'lg', variant: 'outline' }), 'pressable gap-2')}>
          <Smartphone />
          Open Kaç Kaldı
        </a>
      </li>
    </ol>
  )
}

function NotAndroid() {
  return <p className="max-w-xs text-sm text-muted-foreground">Open this page on your Android phone, for example by scanning the QR code with its camera.</p>
}

function Expired() {
  return (
    <p className="max-w-xs text-sm text-muted-foreground">
      This sign-in link has expired. On the device you are already signed in on, open the Android app page again for a new QR code.
    </p>
  )
}
