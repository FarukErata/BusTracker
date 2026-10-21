import { useEffect, useState, type ReactNode } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { Menu } from '@base-ui/react/menu'
import { ArrowLeft, ArrowRight, Moon, RotateCw, Sun, TrafficCone } from 'lucide-react'
import { Badge } from '@/components/reui/badge'
import { Button } from '@/components/ui/button'
import { Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { ago, minutes, titleCase, type ReportAge } from '@/lib/format'
import { LANGS, setLang, useLang, useT } from '@/lib/i18n'
import { useTheme } from '@/lib/theme'
import { cn } from '@/lib/utils'

export function LineBadge({ code, className }: { code: string; className?: string }) {
  return (
    <Badge variant="secondary" className={cn('font-semibold tabular-nums', className)}>
      {code}
    </Badge>
  )
}

export const ageRing = (age: ReportAge) => (age === 'fresh' ? '' : 'ring-2 ring-zinc-500/50')

export function LineBadges({ lines, max = 4 }: { lines: string[]; max?: number }) {
  const shown = lines.slice(0, max)
  return (
    <span className="flex flex-wrap gap-1">
      {shown.map(line => <LineBadge key={line} code={line} />)}
      {lines.length > max && <Badge variant="outline">+{lines.length - max}</Badge>}
    </span>
  )
}

export function ThemeToggle() {
  const { theme, toggle } = useTheme()
  const t = useT()
  return (
    <Button variant="ghost" size="icon" onClick={toggle} aria-label={theme === 'dark' ? t.common.toLight : t.common.toDark}>
      {theme === 'dark' ? <Sun /> : <Moon />}
    </Button>
  )
}

export function LanguageMenu() {
  const lang = useLang()
  const t = useT()
  return (
    <Menu.Root>
      <Menu.Trigger
        aria-label={t.common.language}
        className="pressable h-8 cursor-pointer rounded-md bg-transparent px-2 text-xs font-semibold tracking-wide hover:bg-muted"
      >
        {lang.toUpperCase()}
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Positioner sideOffset={4} align="center" className="z-50">
          <Menu.Popup className="flex origin-[var(--transform-origin)] flex-col gap-0.5 rounded-lg border bg-popover p-1 text-popover-foreground shadow-md outline-none transition-[opacity,scale] duration-150 ease-out data-[ending-style]:scale-95 data-[ending-style]:opacity-0 data-[starting-style]:scale-95 data-[starting-style]:opacity-0">
            {LANGS.map(option => (
              <Menu.Item
                key={option}
                onClick={() => setLang(option)}
                className={cn(
                  'cursor-default rounded-md px-3 py-1.5 text-center text-xs font-semibold tracking-wide outline-none select-none data-[highlighted]:bg-muted',
                  option === lang ? 'bg-muted' : 'text-muted-foreground',
                )}
              >
                {option.toUpperCase()}
              </Menu.Item>
            ))}
          </Menu.Popup>
        </Menu.Positioner>
      </Menu.Portal>
    </Menu.Root>
  )
}

export function BackButton() {
  const navigate = useNavigate()
  const location = useLocation()
  const hasHistory = location.key !== 'default'
  const t = useT()
  return (
    <Button variant="ghost" size="icon" onClick={() => (hasHistory ? navigate(-1) : navigate('/'))} aria-label={t.common.back}>
      <ArrowLeft />
    </Button>
  )
}

export function useNow(intervalMs: number) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), intervalMs)
    return () => clearInterval(timer)
  }, [intervalMs])
  return now
}

export function Minutes({ etaSeconds, receivedAt }: { etaSeconds: number; receivedAt?: number }) {
  const now = useNow(5_000)
  const elapsed = receivedAt ? Math.max(0, (now - receivedAt) / 1000) : 0
  const value = minutes(Math.max(0, etaSeconds - elapsed))
  const t = useT()
  return (
    <span className="flex items-baseline gap-0.5 tabular-nums">
      <span key={value} className="eta-swap text-lg font-semibold leading-none">{value}</span>
      <span className="text-xs text-muted-foreground">{t.common.min}</span>
    </span>
  )
}

export function TrafficBadge() {
  const t = useT()
  return (
    <Badge variant="warning" className="gap-1">
      <TrafficCone className="size-3" aria-hidden />
      {t.common.heavyTraffic}
    </Badge>
  )
}

export function UpdatedAgo({ fetchedAt }: { fetchedAt: string | null | undefined }) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 5_000)
    return () => clearInterval(timer)
  }, [])
  const t = useT()
  if (!fetchedAt) return null
  return <p className="text-center text-xs text-muted-foreground">{t.common.updated(ago((now - Date.parse(fetchedAt)) / 1000))}</p>
}

export function RowsSkeleton({ rows = 4 }: { rows?: number }) {
  return (
    <div className="flex flex-col gap-3 px-3 py-2" aria-busy="true">
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="flex items-center gap-3">
          <Skeleton className="size-8 rounded-full" />
          <div className="flex flex-1 flex-col gap-1.5">
            <Skeleton className="h-3.5 w-2/3" />
            <Skeleton className="h-3 w-1/3" />
          </div>
        </div>
      ))}
    </div>
  )
}

export function ErrorState({ message, onRetry }: { message: string; onRetry: () => void }) {
  const t = useT()
  return (
    <Empty>
      <EmptyHeader>
        <EmptyTitle>{t.common.couldNotLoad}</EmptyTitle>
        <EmptyDescription>{message}</EmptyDescription>
      </EmptyHeader>
      <EmptyContent>
        <Button variant="outline" onClick={onRetry}>
          <RotateCw data-icon="inline-start" />
          {t.common.retry}
        </Button>
      </EmptyContent>
    </Empty>
  )
}

export function EmptyState({ title, description, children }: { title: string; description: string; children?: ReactNode }) {
  return (
    <Empty>
      <EmptyHeader>
        <EmptyTitle>{title}</EmptyTitle>
        <EmptyDescription>{description}</EmptyDescription>
      </EmptyHeader>
      {children && <EmptyContent>{children}</EmptyContent>}
    </Empty>
  )
}

export function SectionTitle({ children }: { children: ReactNode }) {
  return <h2 className="px-3 pt-4 pb-1 text-xs font-medium text-muted-foreground">{children}</h2>
}

export function StopName({ name, district }: { name: string; district?: string | null }) {
  return (
    <span className="flex min-w-0 items-baseline gap-1.5">
      <span className="truncate">{titleCase(name)}</span>
      {district && <span className="shrink-0 text-xs font-normal text-muted-foreground">{titleCase(district)}</span>}
    </span>
  )
}

export function Towards({ stop }: { stop: string | null | undefined }) {
  if (!stop) return null
  return (
    <span className="flex min-w-0 items-center gap-1 text-muted-foreground">
      <ArrowRight className="size-3 shrink-0" aria-hidden />
      <span className="truncate">{titleCase(stop)}</span>
    </span>
  )
}
