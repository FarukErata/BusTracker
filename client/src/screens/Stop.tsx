import { Link, useParams, useSearchParams } from 'react-router'
import { ArrowRight, Star } from 'lucide-react'
import { useAddFavoriteStop, useFavorites, useRemoveFavoriteStop, useStop, useStopArrivals } from '@/api/hooks'
import type { StopArrivalView, StopArrivalsView, StopInfo } from '@/api/types'
import { BackButton, EmptyState, ErrorState, LineBadge, Minutes, RowsSkeleton, StopName, Towards, UpdatedAgo } from '@/components/bits'
import { MapSurface, StopMap } from '@/components/maps'
import { Button } from '@/components/ui/button'
import { Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemMedia, ItemTitle } from '@/components/ui/item'
import { Skeleton } from '@/components/ui/skeleton'
import { busWhere, titleCase } from '@/lib/format'
import { useIsWide } from '@/lib/media'
import { useTracking, type TrackedSelection, type TrackingView } from '@/lib/tracking'
import { cn } from '@/lib/utils'

export function Stop() {
  const { code = '' } = useParams()
  const stop = useStop(code)
  const arrivals = useStopArrivals(code)
  const info = stop.data?.stop
  const wide = useIsWide()
  const [params, setParams] = useSearchParams()
  const selection = readSelection(params)
  const tracking = useTracking(selection, code)
  const select = (next: TrackedSelection | null) => setParams(writeSelection(next), { replace: true })
  const region = <ArrivalsRegion arrivals={arrivals} selection={selection} tracking={tracking} onSelect={select} />

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

  return (
    <Button
      variant="ghost"
      size="icon"
      aria-label={saved ? 'Remove from your stops' : 'Save to your stops'}
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

function oldestFetch(data: StopArrivalsView) {
  return data.lines.map(l => l.data?.fetchedAt).filter((t): t is string => !!t).sort()[0]
}

function ArrivalsList({ data, selection, tracking, onSelect }: { data: StopArrivalsView } & SelectionProps) {
  const waiting = data.lines.filter(l => l.error).map(l => l.line)
  const bothWays = linesInBothDirections(data)
  const isSelected = (a: StopArrivalView) =>
    selection?.line === a.line && selection.direction === a.direction && selection.doorNo === a.bus.doorNo

  return (
    <>
      {data.arrivals.length === 0 ? (
        <EmptyState title="No bus on the way" description={`Lines here: ${[...new Set(data.lines.map(l => l.line))].join(', ')}.`} />
      ) : (
        <ItemGroup>
          {data.arrivals.map(a => (
            <ArrivalRow
              key={`${a.line}-${a.bus.doorNo}`}
              arrival={a}
              stopName={data.stop.name}
              bothWays={bothWays.has(a.line)}
              selected={isSelected(a)}
              tracking={isSelected(a) ? tracking : null}
              onToggle={() => onSelect(isSelected(a) ? null : { line: a.line, direction: a.direction, doorNo: a.bus.doorNo })}
            />
          ))}
        </ItemGroup>
      )}
      {waiting.length > 0 && <p className="px-3 text-xs text-muted-foreground">Still loading: {[...new Set(waiting)].join(', ')}</p>}
      <div className="mt-auto pb-4"><UpdatedAgo fetchedAt={oldestFetch(data)} /></div>
    </>
  )
}

function linesInBothDirections(data: StopArrivalsView) {
  const directions = new Map<string, Set<string>>()
  for (const l of data.lines) directions.set(l.line, (directions.get(l.line) ?? new Set()).add(l.direction))
  return new Set([...directions].filter(([, dirs]) => dirs.size > 1).map(([line]) => line))
}

function arrivalDetail(arrival: StopArrivalView, stopName: string, bothWays: boolean) {
  const { bus } = arrival
  const parts = [
    sameName(arrival.directionName, stopName) && 'Ends here',
    bothWays && !sameName(arrival.directionName, stopName) && `To ${titleCase(arrival.directionName)}`,
    !bus.notDeparted && bus.lastStopName && `now at ${titleCase(bus.lastStopName)}`,
  ].filter(Boolean)
  return parts.join(' · ')
}

function ArrivalRow({ arrival, stopName, bothWays, selected, tracking, onToggle }: {
  arrival: StopArrivalView; stopName: string; bothWays: boolean; selected: boolean; tracking: TrackingView | null; onToggle: () => void
}) {
  const { bus } = arrival
  const detail = arrivalDetail(arrival, stopName, bothWays)
  return (
    <div className={cn('transition-colors', selected && 'bg-muted/60')}>
      <Item
        className="pressable w-full cursor-pointer text-left"
        render={<button type="button" aria-expanded={selected} />}
        onClick={onToggle}
      >
        <ItemMedia><LineBadge code={arrival.line} /></ItemMedia>
        <ItemContent>
          <ItemTitle>{busWhere(bus.notDeparted, bus.stopsAway)}</ItemTitle>
          {detail && <ItemDescription className="first-letter:uppercase">{detail}</ItemDescription>}
        </ItemContent>
        <ItemActions>
          {bus.etaSeconds != null ? <Minutes etaSeconds={bus.etaSeconds} /> : <span className="text-xs text-muted-foreground">Not left</span>}
        </ItemActions>
      </Item>
      {selected && <TrackingDetails arrival={arrival} tracking={tracking} />}
    </div>
  )
}

function stopsSummary(remaining: TrackingView['remaining']) {
  const names = remaining.map(s => titleCase(s.name))
  if (names.length <= 4) return names
  return [...names.slice(0, 2), `${names.length - 3} more`, names.at(-1)!]
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
        See all of line {arrival.line} →
      </Link>
    </div>
  )
}
