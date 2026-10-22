import { useState } from 'react'
import { useParams } from 'react-router'
import { ChevronDown, Clock, Star } from 'lucide-react'
import { useAddFavoriteStation, useFavorites, useRailStation, useRemoveFavoriteStation } from '@/api/hooks'
import type { AnnouncementView, RailDepartureView, RailDirectionView, RailStationResponse } from '@/api/types'
import { BackButton, ErrorState, RailBadge, RowsSkeleton, useNow } from '@/components/bits'
import { MapSurface, StopMap } from '@/components/maps'
import { Announcements, dayWithDate, HourGrid, NextDayDot } from '@/components/planned'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { clock } from '@/lib/format'
import { useLang, useT } from '@/lib/i18n'
import { cn } from '@/lib/utils'

export function RailStation() {
  useLang()
  const { id = '' } = useParams()
  const station = useRailStation(id)
  const info = station.data?.station

  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-md flex-col">
      <header className="flex items-center gap-1 px-1 pt-2">
        <BackButton />
        <div className="flex min-w-0 flex-1 items-center gap-2 px-1">
          {info ? (
            <>
              <RailBadge line={info.line} color={info.color} />
              <h1 className="truncate text-base font-semibold tracking-tight">{info.name}</h1>
            </>
          ) : (
            <Skeleton className="h-5 w-40" />
          )}
        </div>
        {info && <FavoriteStationButton stationId={info.id} />}
      </header>
      <div className="isolate mx-3 mt-2 h-40 overflow-hidden rounded-xl border">
        {info?.lat != null && info.lng != null
          ? <StopMap lat={info.lat} lng={info.lng} className="size-full" railStation={info.id} pinColor={info.color} />
          : <MapSurface />}
      </div>
      <div className="flex flex-1 flex-col gap-2 pt-2 pb-6">
        {station.isPending && <RowsSkeleton rows={2} />}
        {station.isError && <ErrorState message={station.error.message} onRetry={() => station.refetch()} />}
        {station.data && <StationBody data={station.data} />}
      </div>
    </div>
  )
}

function StationBody({ data }: { data: RailStationResponse }) {
  const t = useT()
  const notices: AnnouncementView[] = data.notices.map(n => ({
    line: n.line, kind: 'notice', message: n.text, postedAt: '', direction: null, fromStop: null, departure: null, reason: null,
  }))
  const nextDate = data.directions.find(d => d.next.some(n => n.nextDay))?.nextDate
  return (
    <>
      <Announcements items={notices} showLines={false} />
      {data.directions.map(direction => <DirectionRow key={direction.id} direction={direction} />)}
      {nextDate && (
        <p className="flex items-center gap-1.5 px-3 text-xs text-muted-foreground"><NextDayDot />{t.plan.nextDayTrips(dayWithDate(nextDate))}</p>
      )}
      <p className="px-3 text-xs text-muted-foreground">{t.rail.planned}</p>
      <StationTimetable directions={data.directions} />
    </>
  )
}

function DirectionRow({ direction }: { direction: RailDirectionView }) {
  const t = useT()
  return (
    <section className="flex flex-col gap-1.5 px-3 py-2">
      <div className="flex items-baseline gap-2">
        <h2 className="text-sm font-medium">{t.rail.towards(direction.to)}</h2>
        <span className="truncate text-xs text-muted-foreground">{t.rail.from(direction.from)}</span>
      </div>
      {direction.next.length === 0
        ? <p className="text-sm text-muted-foreground">{t.rail.noTrains}</p>
        : <div className="flex gap-5">{direction.next.map(d => <Departure key={d.at} departure={d} />)}</div>}
    </section>
  )
}

function Departure({ departure }: { departure: RailDepartureView }) {
  const now = useNow(15_000)
  const t = useT()
  const minutes = Math.max(0, Math.round((Date.parse(departure.at) - now) / 60_000))
  return (
    <div className="flex flex-col">
      <span className="relative flex items-baseline gap-0.5 tabular-nums">
        {departure.nextDay && <NextDayDot className="absolute -top-0.5 -left-1.5" />}
        {minutes === 0
          ? <span className="text-lg font-semibold leading-none">{t.rail.now}</span>
          : <><span className="text-lg font-semibold leading-none">{minutes}</span><span className="text-xs text-muted-foreground">{t.common.min}</span></>}
      </span>
      <span className="text-xs text-muted-foreground tabular-nums">{clock(departure.at)}</span>
    </div>
  )
}

function StationTimetable({ directions }: { directions: RailDirectionView[] }) {
  const [open, setOpen] = useState(false)
  const [picked, setPicked] = useState(0)
  const t = useT()
  const direction = directions[Math.min(picked, directions.length - 1)]
  if (!direction) return null
  return (
    <section className="flex flex-col gap-2 pt-3">
      <Button variant="outline" className="pressable mx-3 justify-start gap-2" aria-expanded={open} onClick={() => setOpen(o => !o)}>
        <Clock aria-hidden />
        <span className="flex-1 text-left">{open ? t.plan.hideTimetable : t.plan.showTimetable}</span>
        <ChevronDown className={cn('text-muted-foreground transition-transform duration-200 ease-out', open && 'rotate-180')} aria-hidden />
      </Button>
      {open && (
        <div className="flex flex-col gap-2 px-3">
          {directions.length > 1 && (
            <div role="group" aria-label={t.rail.timetable} className="flex flex-wrap gap-1.5">
              {directions.map((d, i) => (
                <Button key={d.id} size="sm" variant={i === picked ? 'secondary' : 'outline'} aria-pressed={i === picked} className="pressable rounded-full" onClick={() => setPicked(i)}>
                  {t.rail.towards(d.to)}
                </Button>
              ))}
            </div>
          )}
          <HourGrid trips={direction.trips} today />
        </div>
      )}
    </section>
  )
}

function FavoriteStationButton({ stationId }: { stationId: number }) {
  const favorites = useFavorites()
  const add = useAddFavoriteStation()
  const remove = useRemoveFavoriteStation()
  const saved = favorites.data?.stations?.find(s => s.favorite.stationId === stationId)
  const t = useT()
  return (
    <Button
      variant="ghost"
      size="icon"
      aria-label={saved ? t.rail.unsave : t.rail.save}
      aria-pressed={!!saved}
      onClick={() => (saved ? remove.mutate(saved.favorite.id) : add.mutate(stationId))}
      disabled={add.isPending || remove.isPending || favorites.isPending}
    >
      <Star className={cn(saved && 'fill-current text-amber-500 dark:text-amber-400')} />
    </Button>
  )
}
