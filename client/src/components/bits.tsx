import { useEffect, useState, type ReactNode } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { ArrowLeft, ArrowRight, Moon, RotateCw, Sun, TrafficCone } from 'lucide-react'
import { Badge } from '@/components/reui/badge'
import { Button } from '@/components/ui/button'
import { Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { ago, minutes, titleCase } from '@/lib/format'
import { useTheme } from '@/lib/theme'

export function LineBadge({ code }: { code: string }) {
  return (
    <Badge variant="secondary" className="font-semibold tabular-nums">
      {code}
    </Badge>
  )
}

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
  return (
    <Button variant="ghost" size="icon" onClick={toggle} aria-label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}>
      {theme === 'dark' ? <Sun /> : <Moon />}
    </Button>
  )
}

export function BackButton() {
  const navigate = useNavigate()
  const location = useLocation()
  const hasHistory = location.key !== 'default'
  return (
    <Button variant="ghost" size="icon" onClick={() => (hasHistory ? navigate(-1) : navigate('/'))} aria-label="Back">
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
  return (
    <span className="flex items-baseline gap-0.5 tabular-nums">
      <span key={value} className="eta-swap text-lg font-semibold leading-none">{value}</span>
      <span className="text-xs text-muted-foreground">min</span>
    </span>
  )
}

export function TrafficBadge() {
  return (
    <Badge variant="warning" className="gap-1">
      <TrafficCone className="size-3" aria-hidden />
      Heavy traffic
    </Badge>
  )
}

export function UpdatedAgo({ fetchedAt }: { fetchedAt: string | null | undefined }) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 5_000)
    return () => clearInterval(timer)
  }, [])
  if (!fetchedAt) return null
  return <p className="text-center text-xs text-muted-foreground">Updated {ago((now - Date.parse(fetchedAt)) / 1000)}</p>
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
  return (
    <Empty>
      <EmptyHeader>
        <EmptyTitle>Couldn't load this</EmptyTitle>
        <EmptyDescription>{message}</EmptyDescription>
      </EmptyHeader>
      <EmptyContent>
        <Button variant="outline" onClick={onRetry}>
          <RotateCw data-icon="inline-start" />
          Retry
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
