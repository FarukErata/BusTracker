import { useParams, useSearchParams } from 'react-router'
import { useRailLine, useRailStation } from '@/api/hooks'
import type { AnnouncementView } from '@/api/types'
import { BackButton, ErrorState, RailBadge, RowsSkeleton, SectionTitle } from '@/components/bits'
import { MapSurface, RailLineMap } from '@/components/maps'
import { Announcements } from '@/components/planned'
import { Item, ItemContent, ItemGroup, ItemTitle } from '@/components/ui/item'
import { Skeleton } from '@/components/ui/skeleton'
import { useLang, useT } from '@/lib/i18n'
import { cn } from '@/lib/utils'
import { FavoriteStationButton, StationBody } from '@/screens/RailStation'

export function RailLine() {
  useLang()
  const { id = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const picked = Number(params.get('station')) || null
  const line = useRailLine(id)
  const station = useRailStation(picked ? String(picked) : '')
  const t = useT()
  const data = line.data
  const pickedStation = data?.stations.find(s => s.id === picked)
  const pick = (stationId: number | null) => {
    setParams(stationId ? new URLSearchParams({ station: String(stationId) }) : new URLSearchParams(), { replace: true })
    if (stationId) window.scrollTo({ top: 0, behavior: 'smooth' })
  }
  const notices: AnnouncementView[] = (data?.notices ?? []).map(n => ({
    line: n.line, kind: 'notice', message: n.text, postedAt: '', direction: null, fromStop: null, departure: null, reason: null,
  }))

  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-md flex-col">
      <header className="flex items-center gap-1 px-1 pt-2">
        <BackButton />
        <div className="flex min-w-0 flex-1 items-center gap-2 px-1">
          {data ? (
            <>
              <RailBadge line={data.line.name} color={data.line.color} />
              <h1 className="truncate text-base font-semibold tracking-tight">{pickedStation?.name ?? data.line.description}</h1>
            </>
          ) : (
            <Skeleton className="h-5 w-48" />
          )}
        </div>
        {picked && <FavoriteStationButton stationId={picked} />}
      </header>
      <div className="isolate mx-3 mt-2 h-56 overflow-hidden rounded-xl border">
        {data ? <RailLineMap stations={data.stations} color={data.line.color} selected={picked} onPick={pick} /> : <MapSurface />}
      </div>
      <div className="flex flex-1 flex-col gap-2 pt-2 pb-6">
        {line.isPending && <RowsSkeleton rows={4} />}
        {line.isError && <ErrorState message={line.error.message} onRetry={() => line.refetch()} />}
        <Announcements items={notices} showLines={false} />
        {picked && station.isPending && <RowsSkeleton rows={2} />}
        {picked && station.isError && <ErrorState message={station.error.message} onRetry={() => station.refetch()} />}
        {picked && station.data && <StationBody data={station.data} />}
        {data && (
          <>
            <SectionTitle>{t.rail.stations}</SectionTitle>
            <ItemGroup>
              {data.stations.map((s, i) => (
                <Item
                  key={s.id}
                  size="xs"
                  className={cn('pressable cursor-pointer', s.id === picked && 'bg-muted')}
                  render={<button type="button" aria-pressed={s.id === picked} />}
                  onClick={() => pick(s.id === picked ? null : s.id)}
                >
                  <span className="w-6 text-right text-xs text-muted-foreground tabular-nums">{i + 1}</span>
                  <ItemContent><ItemTitle className={cn('font-normal', s.id === picked && 'font-medium')}>{s.name}</ItemTitle></ItemContent>
                </Item>
              ))}
            </ItemGroup>
          </>
        )}
      </div>
    </div>
  )
}
