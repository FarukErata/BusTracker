import { useEffect, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { ArrowRight, Star } from 'lucide-react'
import { useAddFavoriteStop, useFavorites, useRemoveFavoriteStop, useStop, useStopArrivals } from '@/api/hooks'
import type { StopArrivalView, StopArrivalsView, StopInfo } from '@/api/types'
import { ageRing, BackButton, EmptyState, ErrorState, LineBadge, RowsSkeleton, StopName, Towards, TrafficBadge, UpdatedAgo } from '@/components/bits'
import { MapSurface, StopMap } from '@/components/maps'
import { Announcements, ArrivalTime, NextTrips } from '@/components/planned'
import { Button } from '@/components/ui/button'
import { Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemMedia, ItemTitle } from '@/components/ui/item'
import { Skeleton } from '@/components/ui/skeleton'
import { busWhere, clock, reportAge, secondsSince, seenAgo, titleCase } from '@/lib/format'
import { strings, useLang, useT } from '@/lib/i18n'
import { useIsWide } from '@/lib/media'
import { rememberRecent } from '@/lib/recents'
import { useStopLineFilter } from '@/lib/stopFilters'
import { useTracking, type TrackedSelection, type TrackingView } from '@/lib/tracking'
import { cn } from '@/lib/utils'

export function Stop() {
  useLang()
  const { code = '' } = useParams()
  const stop = useStop(code)
  const [picked, setPicked] = useStopLineFilter(code)
  const arrivals = useStopArrivals(code, picked)
  const info = stop.data?.stop
  useEffect(() => {
    if (info) rememberRecent({ kind: 'stop', code: info.code, name: info.name, district: info.district })
  }, [info])
  const wide = useIsWide()
  const [params, setParams] = useSearchParams()
  const selection = readSelection(params)
  const tracking = useTracking(selection, code)
  const select = (next: TrackedSelection | null) => setParams(writeSelection(next), { replace: true })
  const region = <ArrivalsRegion arrivals={arrivals} picked={picked} onPick={setPicked} selection={selection} tracking={tracking} onSelect={select} receivedAt={arrivals.dataUpdatedAt} />

  if (wide)
    return (
      <div className="flex h-dvh w-full overflow-hidden">
        <aside className="flex w-96 shrink-0 flex-col overflow-y-auto border-r">
          <StopHeader code={code} info={info} />
          <div className="flex flex-1 flex-col gap-2 pt-2">
            {region}
          </div>
        </aside>
        <div className="isolate min-w-0 flex-1">
          {info ? <StopMap lat={info.lat} lng={info.lng} className="size-full" tracking={tracking} inline={false} /> : <MapSurface />}
        </div>
      </div>
    )

  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-md flex-col">
      <StopHeader code={code} info={info} />
      <div className={cn('isolate mx-3 mt-2 overflow-hidden rounded-xl border', selection ? 'h-64' : 'h-40')}>
        {info ? <StopMap lat={info.lat} lng={info.lng} className="size-full" tracking={tracking} /> : <MapSurface />}
      </div>
      <div className="flex flex-1 flex-col gap-2 pt-2">{region}</div>
    </div>
  )
}

function readSelection(params: URLSearchParams): TrackedSelection | null {
  const line = params.get('line')
  const direction = params.get('dir')
  const doorNo = params.get('bus')
  return line && direction && doorNo ? { line, direction, doorNo } : null
}

function writeSelection(selection: TrackedSelection | null) {
  return selection ? new URLSearchParams({ line: selection.line, dir: selection.direction, bus: selection.doorNo }) : new URLSearchParams()
}

interface SelectionProps {
  selection: TrackedSelection | null
  tracking: TrackingView | null
  onSelect: (selection: TrackedSelection | null) => void
  picked: string[]
  onPick: (lines: string[]) => void
  receivedAt: number
}

function ArrivalsRegion({ arrivals, ...selectionProps }: { arrivals: ReturnType<typeof useStopArrivals> } & SelectionProps) {
  if (arrivals.isPending) return <RowsSkeleton />
  if (arrivals.isError) return <ErrorState message={arrivals.error.message} onRetry={() => arrivals.refetch()} />
  return <ArrivalsList data={arrivals.data} {...selectionProps} />
}

function StopHeader({ code, info }: { code: string; info: StopInfo | undefined }) {
  return (
    <>
      <header className="flex items-center gap-1 px-1 pt-2">
        <BackButton />
        <div className="flex min-w-0 flex-1 flex-col gap-0.5 px-1">
          {info ? (
            <>
              <h1 className="text-base font-semibold tracking-tight"><StopName name={info.name} district={info.district} /></h1>
              <p className="text-xs"><Towards stop={info.towards} /></p>
            </>
          ) : (
            <Skeleton className="h-5 w-40" />
          )}
        </div>
        <FavoriteStopButton code={code} />
      </header>
    </>
  )
}

function FavoriteStopButton({ code }: { code: string }) {
  const favorites = useFavorites()
  const add = useAddFavoriteStop()
  const remove = useRemoveFavoriteStop()
  const saved = favorites.data?.stops.find(s => s.favorite.stopCode === code)
  const busy = add.isPending || remove.isPending
  const t = useT()

  return (
    <Button
      variant="ghost"
      size="icon"
      aria-label={saved ? t.stop.unsave : t.stop.save}
      aria-pressed={!!saved}
      onClick={() => (saved ? remove.mutate(saved.favorite.id) : add.mutate(code))}
      disabled={busy || favorites.isPending}
    >
      <Star className={cn(saved && 'fill-current text-amber-500 dark:text-amber-400')} />
    </Button>
  )
}

function sameName(a: string, b: string) {
  return a.trim().toLocaleUpperCase('tr-TR') === b.trim().toLocaleUpperCase('tr-TR')
}

function waitingText(lines: StopArrivalsView['lines']) {
  const t = strings().stop
  const names = [...new Set(lines.map(l => l.line))].join(', ')
  const retries = lines.map(l => l.retryInSeconds).filter((s): s is number => s !== null)
  if (retries.length === 0) return t.waiting(names)
  return `${t.waiting(names)} · ${t.nextTry(Math.min(...retries))}`
}

function oldestFetch(lines: StopArrivalsView['lines']) {
  return lines.map(l => l.data?.fetchedAt).filter((t): t is string => !!t).sort()[0]
}

function reportSeconds(arrival: StopArrivalView, lines: StopArrivalsView['lines']) {
  const fetchedAt = lines.find(l => l.line === arrival.line && l.direction === arrival.direction)?.data?.fetchedAt
  return arrival.bus.ageSeconds + (fetchedAt ? secondsSince(fetchedAt) : 0)
}

function shownLines(data: StopArrivalsView, picked: string[]) {
  const all = [...new Set(data.lines.map(l => l.line))]
  const chosen = all.filter(line => picked.includes(line))
  return { all, visible: new Set(chosen.length > 0 ? chosen : all), filtered: chosen.length > 0 }
}

function ArrivalsList({ data, selection, tracking, onSelect, picked, onPick, receivedAt }: { data: StopArrivalsView } & SelectionProps) {
  const { all, visible, filtered } = shownLines(data, picked)
  const lines = data.lines.filter(l => visible.has(l.line))
  const arrivals = data.arrivals.filter(a => visible.has(a.line))
  const waiting = lines.filter(l => l.error)
  const bothWays = linesInBothDirections(lines)
  const announcements = data.announcements.filter(a =>
    visible.has(a.line) && (a.direction === null || lines.some(l => l.line === a.line && l.direction === a.direction)))
  const nextTrips = lines.flatMap(l => l.planned ? [{
    key: `${l.line}-${l.direction}`,
    line: l.line,
    label: bothWays.has(l.line) ? strings().bus.towards(titleCase(l.directionName)) : undefined,
    trips: l.planned.next,
  }] : [])
  const isSelected = (a: StopArrivalView) =>
    selection?.line === a.line && selection.direction === a.direction && selection.doorNo === a.bus.doorNo

  return (
    <>
      {all.length > 1 && <LineFilter lines={all} picked={filtered ? [...visible] : []} onChange={onPick} />}
      <Announcements items={announcements} />
      {arrivals.length === 0 ? (
        <EmptyState title={strings().stop.noBus} description={strings().stop.linesHere([...visible].join(', '))} />
      ) : (
        <ItemGroup>
          {arrivals.map(a => (
            <ArrivalRow
              key={`${a.line}-${a.bus.doorNo}`}
              arrival={a}
              stopName={data.stop.name}
              bothWays={bothWays.has(a.line)}
              ageSeconds={reportSeconds(a, lines)}
              receivedAt={receivedAt}
              selected={isSelected(a)}
              tracking={isSelected(a) ? tracking : null}
              onToggle={() => onSelect(isSelected(a) ? null : { line: a.line, direction: a.direction, doorNo: a.bus.doorNo })}
            />
          ))}
        </ItemGroup>
      )}
      <NextTrips rows={nextTrips} atStop />
      {waiting.length > 0 && <p className="px-3 text-xs text-muted-foreground">{waitingText(waiting)}</p>}
      <div className="mt-auto pb-4"><UpdatedAgo fetchedAt={oldestFetch(lines)} /></div>
    </>
  )
}

function pinnedFirst(lines: string[], pinned: string[]) {
  return [...lines.filter(l => pinned.includes(l)), ...lines.filter(l => !pinned.includes(l))]
}

function LineFilter({ lines, picked, onChange }: { lines: string[]; picked: string[]; onChange: (lines: string[]) => void }) {
  const [pinned] = useState(picked)
  const toggle = (line: string) => {
    const next = picked.includes(line) ? picked.filter(l => l !== line) : [...picked, line]
    onChange(next.length === lines.length ? [] : next)
  }
  const t = useT()
  return (
    <div role="group" aria-label={t.stop.linesToShow} className="flex gap-1.5 overflow-x-auto px-3 pb-1 [scrollbar-width:none]">
      <FilterChip active={picked.length === 0} onClick={() => onChange([])}>{t.stop.all}</FilterChip>
      {pinnedFirst(lines, pinned).map(line => (
        <FilterChip key={line} active={picked.includes(line)} onClick={() => toggle(line)}>{line}</FilterChip>
      ))}
    </div>
  )
}

function FilterChip({ active, onClick, children }: { active: boolean; onClick: () => void; children: string }) {
  return (
    <Button size="sm" variant={active ? 'secondary' : 'outline'} aria-pressed={active} className="pressable shrink-0 rounded-full" onClick={onClick}>
      {children}
    </Button>
  )
}

function linesInBothDirections(lines: StopArrivalsView['lines']) {
  const directions = new Map<string, Set<string>>()
  for (const l of lines) directions.set(l.line, (directions.get(l.line) ?? new Set()).add(l.direction))
  return new Set([...directions].filter(([, dirs]) => dirs.size > 1).map(([line]) => line))
}

function arrivalDetail(arrival: StopArrivalView, stopName: string, bothWays: boolean, ageSeconds: number) {
  const { bus } = arrival
  const t = strings().bus
  const parts = [
    sameName(arrival.directionName, stopName) && t.endsHere,
    bothWays && !sameName(arrival.directionName, stopName) && t.towards(titleCase(arrival.directionName)),
    !bus.notDeparted && bus.lastStopName && t.nowAt(titleCase(bus.lastStopName)),
    bus.notDeparted && bus.planned && strings().plan.leaves(clock(bus.planned.departs)),
    reportAge(ageSeconds) !== 'fresh' && seenAgo(ageSeconds),
  ].filter(Boolean)
  return parts.join(' · ')
}

function ArrivalRow({ arrival, stopName, bothWays, ageSeconds, receivedAt, selected, tracking, onToggle }: {
  arrival: StopArrivalView; stopName: string; bothWays: boolean; ageSeconds: number; receivedAt: number; selected: boolean; tracking: TrackingView | null; onToggle: () => void
}) {
  const { bus } = arrival
  const detail = arrivalDetail(arrival, stopName, bothWays, ageSeconds)
  return (
    <div className={cn('transition-colors', selected && 'bg-muted/60')}>
      <Item
        className="pressable w-full cursor-pointer text-left"
        render={<button type="button" aria-expanded={selected} />}
        onClick={onToggle}
      >
        <ItemMedia><LineBadge code={arrival.line} className={ageRing(reportAge(ageSeconds))} /></ItemMedia>
        <ItemContent>
          <ItemTitle>
            {busWhere(bus.notDeparted, bus.stopsAway)}
            {bus.heavyTraffic && <TrafficBadge />}
          </ItemTitle>
          {detail && <ItemDescription className="first-letter:uppercase">{detail}</ItemDescription>}
        </ItemContent>
        <ItemActions>
          <ArrivalTime bus={bus} receivedAt={receivedAt} />
        </ItemActions>
      </Item>
      {selected && <TrackingDetails arrival={arrival} tracking={tracking} />}
    </div>
  )
}

function stopsSummary(remaining: TrackingView['remaining']) {
  const names = remaining.map(s => titleCase(s.name))
  if (names.length <= 4) return names
  return [...names.slice(0, 2), strings().stop.moreStops(names.length - 3), names.at(-1)!]
}

function TrackingDetails({ arrival, tracking }: { arrival: StopArrivalView; tracking: TrackingView | null }) {
  return (
    <div className="flex flex-col gap-2 px-3 pb-3 pl-14 text-xs text-muted-foreground">
      {tracking ? (
        <p className="flex flex-wrap items-center gap-x-1 gap-y-0.5">
          {stopsSummary(tracking.remaining).map((name, i, all) => (
            <span key={`${name}-${i}`} className="flex items-center gap-1">
              <span className={cn(i === all.length - 1 && 'font-medium text-foreground')}>{name}</span>
              {i < all.length - 1 && <ArrowRight className="size-3" aria-hidden />}
            </span>
          ))}
        </p>
      ) : (
        <Skeleton className="h-3.5 w-3/4" />
      )}
      <Link to={`/lines/${encodeURIComponent(arrival.line)}?dir=${arrival.direction}`} className="w-fit font-medium text-primary underline-offset-4 hover:underline">
        {strings().stop.seeLine(arrival.line)}
      </Link>
    </div>
  )
}
