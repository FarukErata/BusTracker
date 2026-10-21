import { useState } from 'react'
import { ChevronDown, Clock, Megaphone } from 'lucide-react'
import { useLineTimetable } from '@/api/hooks'
import type { AnnouncementView, ArrivalView, DayType, PlannedTripView, TimetableResponse, TimetableTripView } from '@/api/types'
import { LineBadge, Minutes, RowsSkeleton, SectionTitle, useNow } from '@/components/bits'
import { Button } from '@/components/ui/button'
import { ItemTitle } from '@/components/ui/item'
import { clock, sentenceCase, titleCase } from '@/lib/format'
import { strings, useT } from '@/lib/i18n'
import { cn } from '@/lib/utils'

export function announcementText(a: AnnouncementView) {
  if (a.kind === 'cancelled' && a.fromStop && a.departure) return strings().plan.cancelledTrip(titleCase(a.fromStop), a.departure)
  return sentenceCase(a.message)
}

export function Announcements({ items, showLines = true }: { items: AnnouncementView[]; showLines?: boolean }) {
  const [open, setOpen] = useState(false)
  const t = useT()
  if (items.length === 0) return null
  return (
    <div className="mx-3 shrink-0 overflow-hidden rounded-lg border bg-muted/40 text-sm">
      <button
        type="button"
        aria-expanded={open}
        onClick={() => setOpen(o => !o)}
        className="pressable flex w-full items-center gap-2 px-2.5 py-2 text-left"
      >
        <Megaphone className="size-4 shrink-0 text-muted-foreground" aria-hidden />
        <span className="min-w-0 flex-1 truncate">{open || items.length > 1 ? t.plan.announcements(items.length) : announcementText(items[0])}</span>
        <ChevronDown className={cn('size-4 shrink-0 text-muted-foreground transition-transform duration-200 ease-out', open && 'rotate-180')} aria-hidden />
      </button>
      {open && (
        <ul className={cn('grid gap-x-2 gap-y-2 border-t px-2.5 py-2', showLines ? 'grid-cols-[auto_minmax(0,1fr)]' : 'grid-cols-1')}>
          {items.map(a => (
            <li key={`${a.line}-${a.message}`} className="contents">
              {showLines && <span className="flex items-start"><LineBadge code={a.line} /></span>}
              <span className="min-w-0">
                {announcementText(a)}
                {a.postedAt && <span className="ml-1.5 text-xs text-muted-foreground tabular-nums">{a.postedAt}</span>}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

export function PlannedAt({ iso }: { iso: string }) {
  const t = useT()
  return (
    <span className="flex flex-col items-end gap-0.5 leading-none">
      <span className="text-sm font-semibold tabular-nums">{t.plan.about(clock(iso))}</span>
      <span className="text-[10px] text-muted-foreground">{t.plan.planned}</span>
    </span>
  )
}

export function ArrivalTime({ bus, receivedAt }: { bus: ArrivalView; receivedAt: number }) {
  if (bus.etaSeconds != null) return <Minutes etaSeconds={bus.etaSeconds} receivedAt={receivedAt} />
  if (bus.planned?.atStop) return <PlannedAt iso={bus.planned.atStop} />
  return <span className="text-xs text-muted-foreground">{strings().bus.notLeft}</span>
}

export function TripTimes({ trips, atStop }: { trips: PlannedTripView[]; atStop: boolean }) {
  const t = useT()
  return (
    <span className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 text-sm tabular-nums">
      {trips.map(trip => {
        const time = atStop && trip.atStop ? t.plan.about(clock(trip.atStop)) : clock(trip.departs)
        return (
          <span key={`${trip.routeCode}-${trip.departs}`} className="flex items-baseline gap-1">
            <span className={cn('relative', trip.cancelled && 'text-muted-foreground line-through')}>
              {trip.nextDay && <NextDayDot className="absolute -top-0.5 -left-1" />}
              {time}
            </span>
            {trip.cancelled && <span className="text-xs text-destructive">{t.plan.cancelled}</span>}
          </span>
        )
      })}
    </span>
  )
}

function NextDayDot({ className }: { className?: string }) {
  return (
    <span className={cn('size-1.5 shrink-0 rounded-full bg-sky-600 dark:bg-sky-400', className)} role="img" aria-label={strings().plan.nextDay} />
  )
}

export interface NextTripsRow {
  key: string
  line: string
  label?: string
  trips: PlannedTripView[]
  nextDate?: string
}

function dayWithDate(date: string) {
  const [year, month, day] = date.split('-')
  const weekday = (new Date(Date.UTC(+year, +month - 1, +day)).getUTCDay() + 6) % 7
  return `${strings().plan.weekdayNames[weekday]} ${day}.${month}.${year}`
}

const NEXT_TRIPS_SHOWN = 5

const firstTripAt = (row: NextTripsRow, atStop: boolean) => {
  const trip = row.trips.find(t => !t.cancelled) ?? row.trips[0]
  return Date.parse((atStop && trip.atStop) || trip.departs)
}

export function NextTrips({ rows, atStop }: { rows: NextTripsRow[]; atStop: boolean }) {
  const [all, setAll] = useState(false)
  const t = useT()
  const sorted = rows.filter(r => r.trips.length > 0).sort((a, b) => firstTripAt(a, atStop) - firstTripAt(b, atStop))
  const shown = all ? sorted : sorted.slice(0, NEXT_TRIPS_SHOWN)
  const hidden = sorted.length - shown.length
  const nextDate = shown.find(row => row.trips.some(trip => trip.nextDay))?.nextDate
  if (sorted.length === 0) return null
  return (
    <section>
      <SectionTitle>{t.plan.nextTrips}</SectionTitle>
      <ul className="grid grid-cols-[auto_minmax(0,1fr)] items-center gap-x-3 gap-y-3 px-3 py-1.5">
        {shown.map(row => (
          <li key={row.key} className="contents">
            <span className="flex"><LineBadge code={row.line} /></span>
            <span className="flex min-w-0 flex-col gap-0.5">
              {row.label && <span className="truncate text-xs text-muted-foreground">{row.label}</span>}
              <TripTimes trips={row.trips} atStop={atStop} />
            </span>
          </li>
        ))}
      </ul>
      {(hidden > 0 || all) && sorted.length > NEXT_TRIPS_SHOWN && (
        <Button variant="ghost" size="sm" className="pressable mx-1.5 text-muted-foreground" onClick={() => setAll(a => !a)}>
          {all ? t.plan.showLess : t.plan.showMore(hidden)}
        </Button>
      )}
      {nextDate && (
        <p className="flex items-center gap-1.5 px-3 pt-1 text-xs text-muted-foreground"><NextDayDot />{t.plan.nextDayTrips(dayWithDate(nextDate))}</p>
      )}
      {atStop && <p className="px-3 pt-1 text-xs text-muted-foreground">{t.plan.estimatedHere}</p>}
    </section>
  )
}

const DAYS: DayType[] = ['I', 'C', 'P']

function dayName(day: DayType) {
  const t = strings().plan
  return day === 'I' ? t.weekday : day === 'C' ? t.saturday : t.sunday
}

export function TimetableSection({ line, direction }: { line: string; direction: string }) {
  const [open, setOpen] = useState(false)
  const [day, setDay] = useState<DayType | null>(null)
  const timetable = useLineTimetable(open ? line : '', day)
  const t = useT()

  return (
    <section className="flex flex-col gap-2 pt-3">
      <Button variant="outline" className="pressable mx-3 justify-start gap-2" aria-expanded={open} onClick={() => setOpen(o => !o)}>
        <Clock aria-hidden />
        <span className="flex-1 text-left">{open ? t.plan.hideTimetable : t.plan.showTimetable}</span>
        <ChevronDown className={cn('text-muted-foreground transition-transform duration-200 ease-out', open && 'rotate-180')} aria-hidden />
      </Button>
      {open && timetable.isPending && <RowsSkeleton rows={2} />}
      {open && timetable.isError && <p className="px-3 text-sm text-muted-foreground">{t.plan.unavailable}</p>}
      {open && timetable.data && <TimetableBody data={timetable.data} direction={direction} onDay={setDay} />}
    </section>
  )
}

function byHour(trips: TimetableTripView[]) {
  const hours: { key: string; trips: TimetableTripView[] }[] = []
  for (const trip of trips) {
    const key = trip.at.slice(0, 13)
    const last = hours.at(-1)
    if (last?.key === key) last.trips.push(trip)
    else hours.push({ key, trips: [trip] })
  }
  return hours
}

function TimetableBody({ data, direction, onDay }: { data: TimetableResponse; direction: string; onDay: (day: DayType) => void }) {
  const now = useNow(60_000)
  const t = useT()
  const dir = data.directions.find(d => d.direction === direction)
  const today = data.dayType === data.todayDayType
  if (!data.available) return <p className="px-3 text-sm text-muted-foreground">{t.plan.unavailable}</p>

  return (
    <div className="flex flex-col gap-2 px-3">
      <div role="group" aria-label={t.plan.timetable} className="flex gap-1.5">
        {DAYS.map(d => (
          <Button key={d} size="sm" variant={d === data.dayType ? 'secondary' : 'outline'} aria-pressed={d === data.dayType} className="pressable rounded-full" onClick={() => onDay(d)}>
            {dayName(d)}
          </Button>
        ))}
      </div>
      {data.holiday && <p className="text-xs text-muted-foreground">{t.plan.holiday}</p>}
      {dir && <ItemTitle className="text-xs font-medium text-muted-foreground">{t.plan.departuresFrom(titleCase(dir.firstStop))}</ItemTitle>}
      {!dir || dir.trips.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t.plan.noTrips}</p>
      ) : (
        <div className="flex flex-col gap-3 text-sm tabular-nums">
          {byHour(dir.trips).map(hour => (
            <ul key={hour.key} className="grid grid-cols-5 gap-x-2 gap-y-1 sm:grid-cols-6">
              {hour.trips.map(trip => (
                <li
                  key={`${trip.routeCode}-${trip.at}`}
                  title={trip.cancelled ? t.plan.cancelled : undefined}
                  className={cn(
                    today && Date.parse(trip.at) < now && 'text-muted-foreground/50',
                    trip.cancelled && 'text-destructive line-through',
                  )}
                >
                  {trip.time}
                </li>
              ))}
            </ul>
          ))}
        </div>
      )}
    </div>
  )
}
