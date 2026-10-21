import { useEffect, useMemo, useRef } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Download, RefreshCw, Smartphone } from 'lucide-react'
import { encode } from 'uqr'
import { hasDevice } from '@/api/client'
import { useCreateLinkCode } from '@/api/hooks'
import { BackButton } from '@/components/bits'
import { Button, buttonVariants } from '@/components/ui/button'
import { APK_URL, isAndroid, joinUrl, openAppUrl } from '@/lib/androidApp'
import { countdown } from '@/lib/format'
import { strings, useLang } from '@/lib/i18n'
import { useSecondsUntil } from '@/lib/useSecondsUntil'
import { cn } from '@/lib/utils'

const REPO = 'FarukErata/BusTracker'

interface Release {
  tagName: string
  publishedAt: string
}

function useLatestRelease() {
  return useQuery({
    queryKey: ['android-release'],
    queryFn: async (): Promise<Release | null> => {
      const response = await fetch(`https://api.github.com/repos/${REPO}/releases/latest`)
      if (response.status === 404) return null
      if (!response.ok) throw new Error(strings().android.githubFailed(response.status))
      const body = await response.json()
      return { tagName: body.tag_name, publishedAt: body.published_at }
    },
    staleTime: 10 * 60_000,
    retry: false,
  })
}

export function AndroidApp() {
  const release = useLatestRelease()
  const onAndroid = isAndroid()
  const signedIn = hasDevice()

  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-md flex-col">
      <header className="flex items-center gap-2 px-3 pt-3">
        <BackButton />
        <h1 className="flex-1 text-lg font-semibold tracking-tight">{strings().android.title}</h1>
      </header>
      <main className="flex flex-col items-center gap-5 px-6 pt-6 pb-10 text-center">
        <ReleaseLine release={release.data} pending={release.isPending} failed={release.isError} />
        {onAndroid ? <OnPhone signedIn={signedIn} /> : signedIn ? <SignInQr /> : <PlainQr />}
      </main>
    </div>
  )
}

function ReleaseLine({ release, pending, failed }: { release: Release | null | undefined; pending: boolean; failed: boolean }) {
  const lang = useLang()
  const t = strings().android
  if (pending) return <p className="h-5 text-sm text-muted-foreground">{t.checking}</p>
  if (failed) return <p className="h-5 text-sm text-muted-foreground">{t.latest}</p>
  if (!release) return <p className="h-5 text-sm text-muted-foreground">{t.noRelease}</p>

  const version = release.tagName.replace(/^android-v/, '')
  const date = new Date(release.publishedAt).toLocaleDateString(lang, { day: 'numeric', month: 'short', year: 'numeric' })
  return <p className="h-5 text-sm text-muted-foreground">{t.version(version, date)}</p>
}

function OnPhone({ signedIn }: { signedIn: boolean }) {
  const create = useCreateLinkCode()
  const openSignedIn = () =>
    create.mutate(undefined, { onSuccess: code => { window.location.href = openAppUrl(code.code) } })

  return (
    <>
      <DownloadButton />
      <p className="max-w-xs text-sm text-muted-foreground">
        {strings().android.afterDownload}
      </p>
      {signedIn && (
        <div className="flex flex-col items-center gap-2 pt-2">
          <Button variant="outline" size="lg" className="pressable gap-2" disabled={create.isPending} onClick={openSignedIn}>
            <Smartphone />
            {strings().android.openSignedIn}
          </Button>
          <p className="max-w-xs text-sm text-muted-foreground">
            {strings().android.openSignedInText}
          </p>
          {create.isError && <p className="text-sm text-destructive">{create.error.message}</p>}
        </div>
      )}
    </>
  )
}

function PlainQr() {
  return (
    <>
      <QrCode value={APK_URL} label={strings().android.qrDownload} />
      <p className="max-w-xs text-sm text-muted-foreground">
        {strings().android.scanDownload}
      </p>
      <ApkLink />
    </>
  )
}

function SignInQr() {
  const create = useCreateLinkCode()
  const { mutate } = create
  const started = useRef(false)
  useEffect(() => {
    if (started.current) return
    started.current = true
    mutate()
  }, [mutate])
  const code = create.data
  const remaining = useSecondsUntil(code?.expiresAt)

  if (create.isError) return <PlainQr />
  if (!code) return <div className="size-64 animate-pulse rounded-2xl bg-muted" aria-busy="true" />
  if (remaining <= 0)
    return (
      <div className="flex flex-col items-center gap-3">
        <p className="text-sm text-muted-foreground">{strings().android.qrExpired}</p>
        <Button className="pressable gap-2" onClick={() => create.mutate()}>
          <RefreshCw />
          {strings().android.newQr}
        </Button>
      </div>
    )

  return (
    <>
      <QrCode value={joinUrl(code.code, code.expiresAt)} label={strings().android.qrSignIn} />
      <p className="max-w-xs text-sm text-muted-foreground">
        {strings().android.scanSignIn}
      </p>
      <p className="text-sm text-muted-foreground tabular-nums">{strings().join.worksOnce(countdown(remaining))}</p>
      <ApkLink />
    </>
  )
}

function ApkLink() {
  return (
    <a href={APK_URL} className="text-sm text-muted-foreground underline underline-offset-4 hover:text-foreground">
      {strings().android.apkLink}
    </a>
  )
}

function DownloadButton() {
  return (
    <a href={APK_URL} className={cn(buttonVariants({ size: 'lg' }), 'pressable gap-2')}>
      <Download />
      {strings().join.download}
    </a>
  )
}

function QrCode({ value, label }: { value: string; label: string }) {
  const { path, size } = useMemo(() => {
    const qr = encode(value, { ecc: 'M', border: 0 })
    const cells = qr.data.flatMap((row, y) => row.map((dark, x) => (dark ? `M${x} ${y}h1v1h-1z` : '')))
    return { path: cells.join(''), size: qr.size }
  }, [value])

  return (
    <div className="rounded-2xl bg-white p-4 shadow-sm ring-1 ring-black/5">
      <svg viewBox={`0 0 ${size} ${size}`} className="size-56" shapeRendering="crispEdges" role="img" aria-label={label}>
        <path d={path} fill="#000" />
      </svg>
    </div>
  )
}
