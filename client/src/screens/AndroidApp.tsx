import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Download } from 'lucide-react'
import { encode } from 'uqr'
import { BackButton } from '@/components/bits'
import { buttonVariants } from '@/components/ui/button'
import { cn } from '@/lib/utils'

const REPO = 'FarukErata/BusTracker'
const APK_URL = `https://github.com/${REPO}/releases/latest/download/kackaldi.apk`

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
      if (!response.ok) throw new Error(`GitHub answered ${response.status}`)
      const body = await response.json()
      return { tagName: body.tag_name, publishedAt: body.published_at }
    },
    staleTime: 10 * 60_000,
    retry: false,
  })
}

const isAndroid = () => /android/i.test(navigator.userAgent)

export function AndroidApp() {
  const release = useLatestRelease()
  const onAndroid = isAndroid()

  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-md flex-col">
      <header className="flex items-center gap-2 px-3 pt-3">
        <BackButton />
        <h1 className="flex-1 text-lg font-semibold tracking-tight">Android app</h1>
      </header>
      <main className="flex flex-col items-center gap-5 px-6 pt-6 pb-10 text-center">
        <ReleaseLine release={release.data} pending={release.isPending} failed={release.isError} />
        {onAndroid ? <DownloadButton /> : <QrCode value={APK_URL} />}
        <p className="max-w-xs text-sm text-muted-foreground">
          {onAndroid
            ? 'After the download, open the file. Android asks once to allow installs from your browser; allow it, then tap Install.'
            : 'Scan with your Android phone’s camera to download the app. Updates install over the old version.'}
        </p>
        {!onAndroid && (
          <a href={APK_URL} className="text-sm text-muted-foreground underline underline-offset-4 hover:text-foreground">
            Or download the APK here
          </a>
        )}
      </main>
    </div>
  )
}

function ReleaseLine({ release, pending, failed }: { release: Release | null | undefined; pending: boolean; failed: boolean }) {
  if (pending) return <p className="h-5 text-sm text-muted-foreground">Checking the latest version…</p>
  if (failed) return <p className="h-5 text-sm text-muted-foreground">Latest version</p>
  if (!release) return <p className="h-5 text-sm text-muted-foreground">No release published yet</p>

  const version = release.tagName.replace(/^android-v/, '')
  const date = new Date(release.publishedAt).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
  return <p className="h-5 text-sm text-muted-foreground">Version {version} · {date}</p>
}

function DownloadButton() {
  return (
    <a href={APK_URL} className={cn(buttonVariants({ size: 'lg' }), 'pressable gap-2')}>
      <Download />
      Download Kaç Kaldı
    </a>
  )
}

function QrCode({ value }: { value: string }) {
  const { path, size } = useMemo(() => {
    const qr = encode(value, { ecc: 'M', border: 0 })
    const cells = qr.data.flatMap((row, y) => row.map((dark, x) => (dark ? `M${x} ${y}h1v1h-1z` : '')))
    return { path: cells.join(''), size: qr.size }
  }, [value])

  return (
    <div className="rounded-2xl bg-white p-4 shadow-sm ring-1 ring-black/5">
      <svg viewBox={`0 0 ${size} ${size}`} className="size-56" shapeRendering="crispEdges" role="img" aria-label="QR code for the Android app download">
        <path d={path} fill="#000" />
      </svg>
    </div>
  )
}
