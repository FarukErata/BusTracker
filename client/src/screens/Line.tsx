import { useMemo, useState } from 'react'
import { useParams, useSearchParams } from 'react-router'
import { ArrowLeftRight, MapPin, Star, X } from 'lucide-react'
import { useAddFavoriteLine, useBusPath, useFavorites, useLineArrivals, useLineLive, useLineShapes, useLineStops, useRemoveFavoriteLine } from '@/api/hooks'
import type { ArrivalView, LineDirection, LineStop, TrackedBusView } from '@/api/types'
import { BackButton, ErrorState, LineBadge, Minutes, RowsSkeleton, UpdatedAgo } from '@/components/bits'
import { LineMap } from '@/components/maps'
import { Button } from '@/components/ui/button'
import { Drawer, DrawerContent, DrawerTitle } from '@/components/ui/drawer'
import { Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemTitle } from '@/components/ui/item'
import { distance, reportAge, secondsSince, seenAgo, stopsAway, titleCase } from '@/lib/format'
import { useIsWide } from '@/lib/media'
import { cn } from '@/lib/utils'

const PEEK = '180px'
const HALF = 0.5
const SNAP_POINTS = [PEEK, HALF, 0.92]
const VISIBLE: TrackedBusView['status'][] = ['ok', 'at-terminal', 'stale']

function snapHeight(snap: string | number) {
  return typeof snap === 'number' ? Math.round(window.innerHeight * snap) : parseInt(snap, 10)
}

export function Line() {
  const { code = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const [selectedBus, setSelectedBus] = useState<string | null>(() => params.get('bus'))
  const [snap, setSnap] = useState<string | number | null>(() => (params.get('stop') ? HALF : PEEK))
  const wide = useIsWide()
  const stops = useLineStops(code)
  const shapes = useLineShapes(code)
  const live = useLineLive(code)

  const directions = stops.data?.directions ?? []
  const direction = directions.find(d => d.direction === params.get('dir')) ?? directions[0]
  const pinnedStop = params.get('stop')
  const arrivals = useLineArrivals(code, pinnedStop, direction?.direction ?? null)

  const route = useMemo<[number, number][]>(() => {
    const shape = shapes.data?.shapes.find(s => s.direction === direction?.direction && s.variant === 'D0')
    return shape?.points ?? direction?.stops.map(s => [s.lat, s.lng] as [number, number]) ?? []
  }, [shapes.data, direction])
  const buses = (live.data?.buses ?? []).filter(b => b.direction === direction?.direction && VISIBLE.includes(b.status))
  const selected = buses.find(b => b.doorNo === selectedBus)
  const busPath = useBusPath(code, pinnedStop && selectedBus, pinnedStop)
  const highlight = busPath.data && !busPath.data.passed && busPath.data.bus.doorNo === selectedBus ? busPath.data.path : undefined
  const busRoute = selected && selected.variant !== 'D0' ? shapes.data?.shapes.find(s => s.routeCode === selected.routeCode)?.points : undefined

  const update = (next: Record<string, string | null>) => {
    const merged = new URLSearchParams(params)
    Object.entries(next).forEach(([k, v]) => (v ? merged.set(k, v) : merged.delete(k)))
    setParams(merged, { replace: true })
  }
  const pin = (stop: string | null) => {
    update({ stop })
    if (stop) setSnap(current => (current === PEEK ? HALF : current))
  }
  const toggleDirection = () => {
    const i = directions.findIndex(d => d.direction === direction?.direction)
    update({ dir: directions[(i + 1) % directions.length]?.direction ?? null, stop: null })
    setSelectedBus(null)
  }

  if (stops.isError) return <ErrorState message={stops.error.message} onRetry={() => stops.refetch()} />

  const map = (
    <LineMap
      route={route}
      stops={direction?.stops ?? []}
      buses={buses}
      pinnedStop={pinnedStop}
      selectedBus={selectedBus}
      onStopClick={pin}
      onBusClick={setSelectedBus}
      bottomInset={wide ? 0 : snapHeight(snap ?? PEEK)}
      fitId={code}
      ready={route.length > 0 && !live.isPending}
      highlight={highlight}
      busRoute={busRoute}
      controls={wide}
    />
  )
  const header = <LineHeader code={code} direction={direction} canToggle={directions.length > 1} onToggle={toggleDirection} />
  const panel = (
    <LinePanel
      loading={stops.isPending || live.isPending}
      direction={direction}
      buses={buses}
      pinnedStop={pinnedStop}
      arrivals={arrivals}
      selectedBus={selectedBus}
      onSelect={setSelectedBus}
      onPin={pin}
      fetchedAt={live.data?.data.fetchedAt}
    />
  )

  if (wide)
    return (
      <div className="flex h-dvh w-full overflow-hidden">
        <aside className="flex w-96 shrink-0 flex-col border-r">
          <div className="border-b p-1">{header}</div>
          <div className="flex min-h-0 flex-1 flex-col gap-1 overflow-y-auto py-2">{panel}</div>
        </aside>
        <div className="relative isolate min-w-0 flex-1">{map}</div>
      </div>
    )

  return (
    <div className="relative h-dvh w-full overflow-hidden">
      <div className="absolute inset-0 isolate">{map}</div>
      <div className="absolute inset-x-2 top-2 z-10 mx-auto max-w-md rounded-xl border bg-background/95 p-1 backdrop-blur-sm">{header}</div>
      <Drawer open modal={false} snapPoints={SNAP_POINTS} snapPoint={snap} onSnapPointChange={setSnap} showSwipeHandle onOpenChange={() => {}}>
        <DrawerContent className="mx-auto max-w-md">
          <DrawerTitle className="sr-only">Buses on line {code}</DrawerTitle>
          <div className="flex min-h-0 flex-1 flex-col gap-1 overflow-y-auto pb-6">{panel}</div>
        </DrawerContent>
      </Drawer>
    </div>
  )
}

function LinePanel({ loading, direction, buses, pinnedStop, arrivals, selectedBus, onSelect, onPin, fetchedAt }: {
  loading: boolean; direction: LineDirection | undefined; buses: TrackedBusView[]; pinnedStop: string | null
  arrivals: ReturnType<typeof useLineArrivals>; selectedBus: string | null
  onSelect: (door: string) => void; onPin: (stop: string | null) => void; fetchedAt: string | undefined
}) {
  if (loading) return <RowsSkeleton rows={3} />
  return (
    <>
      {pinnedStop && direction ? (
        <PinnedArrivals
          stop={direction.stops.find(s => s.code === pinnedStop)}
          approaching={arrivals.data?.approaching}
          passedCount={arrivals.data?.recentlyPassed.length ?? 0}
          loading={arrivals.isPending}
          onClear={() => onPin(null)}
          onSelect={onSelect}
          selectedBus={selectedBus}
        />
      ) : (
        <BusPositions buses={buses} direction={direction} selectedBus={selectedBus} onSelect={onSelect} onPick={onPin} />
      )}
      <UpdatedAgo fetchedAt={fetchedAt} />
    </>
  )
}

function LineHeader({ code, direction, canToggle, onToggle }: { code: string; direction: LineDirection | undefined; canToggle: boolean; onToggle: () => void }) {
  const first = direction?.stops[0]
  const last = direction?.stops.at(-1)
  return (
    <div className="flex items-center gap-1">
      <BackButton />
      <LineBadge code={code} />
      <button
        type="button"
        onClick={onToggle}
        disabled={!canToggle}
        className="pressable flex min-w-0 flex-1 items-center gap-1.5 rounded-lg px-2 py-1.5 text-left text-sm transition-colors hover:bg-muted"
        aria-label="Switch direction"
      >
        <span className="truncate">{first && last ? `${titleCase(first.name)} → ${titleCase(last.name)}` : 'Loading…'}</span>
        {canToggle && <ArrowLeftRight className="size-4 shrink-0 text-muted-foreground" aria-hidden />}
      </button>
      <FavoriteLineButton code={code} direction={direction?.direction ?? null} />
    </div>
  )
}

function FavoriteLineButton({ code, direction }: { code: string; direction: string | null }) {
  const favorites = useFavorites()
  const add = useAddFavoriteLine()
  const remove = useRemoveFavoriteLine()
  const saved = favorites.data?.lines.find(l => l.favorite.lineCode === code && l.favorite.direction === direction)
  const toggle = () => {
    if (add.isPending || remove.isPending || !direction) return
    if (saved) remove.mutate(saved.favorite.id)
    else add.mutate({ line: code, direction })
  }
  return (
    <Button variant="ghost" size="icon" onClick={toggle} aria-pressed={!!saved} aria-label={saved ? 'Remove from your lines' : 'Save this direction to your lines'}>
      <Star className={cn(saved && 'fill-current text-amber-500 dark:text-amber-400')} />
    </Button>
  )
}

function stopName(direction: LineDirection | undefined, seq: number | null) {
  const stop = seq == null ? undefined : direction?.stops.find(s => s.seq === seq)
  return stop ? titleCase(stop.name) : null
}

function BusPositions({ buses, direction, selectedBus, onSelect, onPick }: {
  buses: TrackedBusView[]; direction: LineDirection | undefined; selectedBus: string | null; onSelect: (door: string) => void; onPick: (stop: string) => void
}) {
  const ordered = [...buses].sort((a, b) => (b.alongMeters ?? 0) - (a.alongMeters ?? 0))
  return (
    <>
      <p className="mx-3 flex items-center gap-1.5 rounded-lg border border-dashed px-2.5 py-1.5 text-xs text-muted-foreground">
        <MapPin className="size-3.5" aria-hidden /> Tap a stop to see arrival times
      </p>
      {ordered.length === 0 ? (
        <p className="px-3 py-4 text-center text-sm text-muted-foreground">No bus is running in this direction right now.</p>
      ) : (
        <ItemGroup>
          {ordered.map(bus => (
            <Item key={bus.doorNo} size="sm" className={cn('pressable cursor-pointer', bus.doorNo === selectedBus && 'bg-muted')} onClick={() => onSelect(bus.doorNo)}>
              <ItemContent>
                <ItemTitle className={cn('tabular-nums', reportAge(secondsSince(bus.reportedAt)) !== 'fresh' && 'opacity-60')}>{bus.doorNo}</ItemTitle>
                <ItemDescription className="first-letter:uppercase">{describePosition(bus, direction)}</ItemDescription>
              </ItemContent>
            </Item>
          ))}
        </ItemGroup>
      )}
      <StopPicker stops={direction?.stops ?? []} onPick={onPick} />
    </>
  )
}

function describePosition(bus: TrackedBusView, direction: LineDirection | undefined) {
  const age = secondsSince(bus.reportedAt)
  return [busPlace(bus, direction), reportAge(age) !== 'fresh' && seenAgo(age)].filter(Boolean).join(' · ')
}

function busPlace(bus: TrackedBusView, direction: LineDirection | undefined) {
  if (bus.status === 'at-terminal') return `Waiting at ${stopName(direction, direction?.stops[0]?.seq ?? null) ?? 'the first stop'}`
  const after = stopName(direction, bus.lastStopSeq)
  const next = stopName(direction, bus.nextStopSeq)
  return [after && `after ${after}`, next && `next ${next}`].filter(Boolean).join(' · ')
}

function StopPicker({ stops, onPick }: { stops: LineStop[]; onPick: (stop: string) => void }) {
  return (
    <>
      <h2 className="px-3 pt-3 pb-1 text-xs font-medium text-muted-foreground">Stops</h2>
      <ItemGroup>
        {stops.map(stop => (
          <Item key={stop.code} size="xs" className="pressable cursor-pointer" onClick={() => onPick(stop.code)}>
            <span className="w-6 text-right text-xs text-muted-foreground tabular-nums">{stop.seq}</span>
            <ItemContent><ItemTitle className="font-normal">{titleCase(stop.name)}</ItemTitle></ItemContent>
          </Item>
        ))}
      </ItemGroup>
    </>
  )
}

function PinnedArrivals({ stop, approaching, passedCount, loading, onClear, onSelect, selectedBus }: {
  stop: LineStop | undefined; approaching: ArrivalView[] | undefined; passedCount: number; loading: boolean
  onClear: () => void; onSelect: (door: string) => void; selectedBus: string | null
}) {
  return (
    <>
      <div className="mx-3 flex items-center gap-1.5 rounded-lg bg-primary/10 py-1 pr-1 pl-2.5 text-sm text-primary">
        <MapPin className="size-3.5 shrink-0" aria-hidden />
        <span className="min-w-0 flex-1 truncate">To {stop ? titleCase(stop.name) : 'this stop'}</span>
        <Button variant="ghost" size="icon" onClick={onClear} aria-label="Clear stop"><X /></Button>
      </div>
      {loading && <RowsSkeleton rows={3} />}
      {approaching && approaching.length === 0 && <p className="px-3 py-4 text-center text-sm text-muted-foreground">No bus on the way to this stop.</p>}
      {approaching && approaching.length > 0 && (
        <ItemGroup>
          {approaching.map(a => (
            <Item key={a.doorNo} size="sm" className={cn('pressable cursor-pointer', a.doorNo === selectedBus && 'bg-muted')} onClick={() => onSelect(a.doorNo)}>
              <ItemContent>
                <ItemTitle className="tabular-nums">{a.doorNo}</ItemTitle>
                <ItemDescription>{a.notDeparted ? 'At the first stop' : [stopsAway(a.stopsAway), distance(a.distanceMeters), a.lastStopName && `now at ${titleCase(a.lastStopName)}`].filter(Boolean).join(' · ')}</ItemDescription>
              </ItemContent>
              <ItemActions>
                {a.etaSeconds != null ? <Minutes etaSeconds={a.etaSeconds} /> : <span className="text-xs text-muted-foreground">Not left</span>}
              </ItemActions>
            </Item>
          ))}
        </ItemGroup>
      )}
      {passedCount > 0 && <p className="px-3 text-xs text-muted-foreground">{passedCount} recently passed this stop</p>}
    </>
  )
}
