import { useDeferredValue, useState, type FocusEvent } from 'react'
import { Link } from 'react-router'
import { Bus, ChevronRight, History, MapPin, Search, Smartphone, TrainFront, UserRound, X } from 'lucide-react'
import { useFavorites, useLineSearch, useRailSearch, useStopSearch } from '@/api/hooks'
import type { FavoritesResponse, LineSummary, RailStationView, StopSearchResult } from '@/api/types'
import { EmptyState, ErrorState, LanguageMenu, LineBadge, LineBadges, Marquee, RailBadge, RowsSkeleton, SectionTitle, StopName, ThemeToggle, Towards, useNow } from '@/components/bits'
import { Announcements } from '@/components/planned'
import { Button, buttonVariants } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemMedia, ItemTitle } from '@/components/ui/item'
import { directionLabel, titleCase } from '@/lib/format'
import { useLang, useT } from '@/lib/i18n'
import { clearRecents, useRecents, type Recent } from '@/lib/recents'

export function Home() {
  useLang()
  const [query, setQuery] = useState('')
  const deferred = useDeferredValue(query)
  const searching = query.trim().length > 0
  const [focused, setFocused] = useState(false)
  const recents = useRecents()
  const leave = (e: FocusEvent<HTMLDivElement>) => {
    if (!e.currentTarget.contains(e.relatedTarget)) setFocused(false)
  }

  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-md flex-col">
      <HomeHeader />
      <div onBlur={leave} className="flex flex-1 flex-col">
        <SearchBox query={query} onChange={setQuery} onFocus={() => setFocused(true)} />
        {searching ? <SearchResults query={deferred} /> : focused && recents.length > 0 ? <Recents items={recents} /> : <Favorites />}
      </div>
    </div>
  )
}

function HomeHeader() {
  const t = useT()
  return (
    <header className="flex items-center gap-2 px-3 pt-3">
      <h1 className="flex-1 text-lg font-semibold tracking-tight">Kaç Kaldı</h1>
      <LanguageMenu />
      <Link to="/android" className={buttonVariants({ variant: 'ghost', size: 'icon' })} aria-label={t.home.androidApp}>
        <Smartphone />
      </Link>
      <Link to="/devices" className={buttonVariants({ variant: 'ghost', size: 'icon' })} aria-label={t.home.devices}>
        <UserRound />
      </Link>
      <ThemeToggle />
    </header>
  )
}

function SearchBox({ query, onChange, onFocus }: { query: string; onChange: (query: string) => void; onFocus: () => void }) {
  const t = useT()
  return (
    <div className="px-3 pt-2">
      <div className="relative">
        <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
        <Input
          value={query}
          onChange={e => onChange(e.target.value)}
          onFocus={onFocus}
          autoComplete="off"
          autoCorrect="off"
          autoCapitalize="off"
          spellCheck={false}
          enterKeyHint="search"
          onKeyDown={e => e.key === 'Escape' && e.currentTarget.blur()}
          placeholder={t.home.placeholder}
          className="pr-9 pl-8"
          aria-label={t.home.search}
        />
        {query.trim().length > 0 && (
          <Button variant="ghost" size="icon" className="absolute top-1/2 right-0.5 -translate-y-1/2" onClick={() => onChange('')} aria-label={t.home.clearSearch}>
            <X />
          </Button>
        )}
      </div>
    </div>
  )
}

function Recents({ items }: { items: Recent[] }) {
  const t = useT()
  return (
    <div onMouseDown={e => e.preventDefault()}>
      <div className="flex items-end justify-between pr-1.5">
        <SectionTitle>{t.home.recent}</SectionTitle>
        <Button variant="ghost" size="sm" className="text-muted-foreground" onClick={clearRecents}>{t.home.clear}</Button>
      </div>
      <ItemGroup>{items.map(r => <RecentRow key={`${r.kind}-${r.code}-${r.kind === 'line' ? r.direction : ''}`} item={r} />)}</ItemGroup>
    </div>
  )
}

function RecentRow({ item }: { item: Recent }) {
  const target = item.kind === 'stop' ? `/stops/${item.code}` : `/lines/${item.code}?dir=${item.direction}`
  return (
    <Item className="pressable" render={<Link to={target} />}>
      {item.kind === 'stop'
        ? <ItemMedia variant="icon"><History className="text-muted-foreground" /></ItemMedia>
        : <ItemMedia><LineBadge code={item.code} /></ItemMedia>}
      <ItemContent>
        <ItemTitle>{item.kind === 'stop' ? <StopName name={item.name} district={item.district} /> : item.title}</ItemTitle>
      </ItemContent>
      <ItemActions><ChevronRight className="size-4 text-muted-foreground" /></ItemActions>
    </Item>
  )
}

function Favorites() {
  const favorites = useFavorites()
  const t = useT()
  if (favorites.isPending) return <RowsSkeleton />
  if (favorites.isError) return <ErrorState message={favorites.error.message} onRetry={() => favorites.refetch()} />

  const { stops, lines } = favorites.data
  const stations = favorites.data.stations ?? []
  if (stops.length === 0 && lines.length === 0 && stations.length === 0)
    return <EmptyState title={t.home.emptyTitle} description={t.home.emptyText} />

  const notices = favorites.data.announcements ?? []
  return (
    <>
      {notices.length > 0 && (
        <>
          <SectionTitle>{t.home.announcements}</SectionTitle>
          <Announcements items={notices} />
        </>
      )}
      {stops.length > 0 && <SectionTitle>{t.home.yourStops}</SectionTitle>}
      <ItemGroup>{stops.map(s => <FavoriteStopRow key={s.favorite.id} item={s} />)}</ItemGroup>
      {stations.length > 0 && <SectionTitle>{t.home.yourStations}</SectionTitle>}
      <ItemGroup>{stations.map(s => <FavoriteStationRow key={s.favorite.id} item={s} />)}</ItemGroup>
      {lines.length > 0 && <SectionTitle>{t.home.yourLines}</SectionTitle>}
      <ItemGroup>{lines.map(l => <FavoriteLineRow key={l.favorite.id} item={l} />)}</ItemGroup>
    </>
  )
}

function FavoriteStopRow({ item }: { item: FavoritesResponse['stops'][number] }) {
  const name = item.stop?.name ?? item.favorite.stopName
  return (
    <Item className="pressable" render={<Link to={`/stops/${item.favorite.stopCode}`} />}>
      <ItemMedia variant="icon"><MapPin className="text-muted-foreground" /></ItemMedia>
      <ItemContent>
        <ItemTitle>{item.favorite.label ?? <StopName name={name} district={item.stop?.district} />}</ItemTitle>
        {item.favorite.label && <ItemDescription><StopName name={name} district={item.stop?.district} /></ItemDescription>}
        {item.stop?.towards && <ItemDescription><Towards stop={item.stop.towards} /></ItemDescription>}
        <LineBadges lines={item.lines} />
      </ItemContent>
      <ItemActions><ChevronRight className="size-4 text-muted-foreground" /></ItemActions>
    </Item>
  )
}

function FavoriteStationRow({ item }: { item: FavoritesResponse['stations'][number] }) {
  const now = useNow(15_000)
  const t = useT()
  const station = item.station
  const minutes = (at: string) => Math.max(0, Math.round((Date.parse(at) - now) / 60_000))
  return (
    <Item className="pressable" render={<Link to={`/rail/${item.favorite.stationId}`} />}>
      <ItemMedia><RailBadge line={item.favorite.line} color={station?.color ?? '#808080'} /></ItemMedia>
      <ItemContent>
        <ItemTitle>{station?.name ?? item.favorite.stationName}</ItemTitle>
        {item.directions.map(d => (
          <ItemDescription key={d.id} className="tabular-nums">
            {t.rail.towards(d.to)} · {d.next.slice(0, 2).map(n => minutes(n.at)).join(', ')} {t.common.min}
          </ItemDescription>
        ))}
      </ItemContent>
      <ItemActions><ChevronRight className="size-4 text-muted-foreground" /></ItemActions>
    </Item>
  )
}

function FavoriteLineRow({ item }: { item: FavoritesResponse['lines'][number] }) {
  const { lineCode, direction, label } = item.favorite
  const dir = direction ? item.directions[direction] : null
  const target = direction ? `/lines/${lineCode}?dir=${direction}` : `/lines/${lineCode}`
  const t = useT()
  return (
    <Item className="pressable" render={<Link to={target} />}>
      <ItemMedia><LineBadge code={lineCode} /></ItemMedia>
      <ItemContent>
        <ItemTitle>{label ?? (dir ? titleCase(dir) : t.home.bothDirections)}</ItemTitle>
      </ItemContent>
      <ItemActions><ChevronRight className="size-4 text-muted-foreground" /></ItemActions>
    </Item>
  )
}

function SearchResults({ query }: { query: string }) {
  const lines = useLineSearch(query)
  const stops = useStopSearch(query)
  const rail = useRailSearch(query)
  const lineResults = lines.data ?? []
  const stopResults = query.trim().length >= 2 ? stops.data ?? [] : []
  const railResults = query.trim().length >= 2 ? rail.data ?? [] : []
  const loading = (lines.isPending && lines.fetchStatus !== 'idle') || (stops.isPending && stops.fetchStatus !== 'idle')
  const t = useT()

  if (loading && lineResults.length === 0 && stopResults.length === 0) return <RowsSkeleton rows={3} />
  if (lines.isError || stops.isError)
    return <ErrorState message={t.home.searchDown} onRetry={() => { lines.refetch(); stops.refetch() }} />
  if (lineResults.length === 0 && stopResults.length === 0 && railResults.length === 0)
    return <EmptyState title={t.home.nothingFound} description={t.home.nothingFoundText} />

  return (
    <>
      {lineResults.length > 0 && <SectionTitle>{t.home.lines}</SectionTitle>}
      <ItemGroup>{lineResults.map(l => <LineResultRow key={l.lineCode} line={l} />)}</ItemGroup>
      {railResults.length > 0 && <SectionTitle>{t.home.rail}</SectionTitle>}
      <ItemGroup>{railResults.map(s => <RailResultRow key={s.id} station={s} />)}</ItemGroup>
      {stopResults.length > 0 && <SectionTitle>{t.home.stops}</SectionTitle>}
      <ItemGroup>{stopResults.map(s => <StopResultRow key={s.stop.code} result={s} />)}</ItemGroup>
    </>
  )
}

function RailResultRow({ station }: { station: RailStationView }) {
  return (
    <Item className="pressable" render={<Link to={`/rail/${station.id}`} />}>
      <ItemMedia variant="icon"><TrainFront style={{ color: station.color }} /></ItemMedia>
      <ItemContent>
        <ItemTitle className="w-full min-w-0 gap-2"><RailBadge line={station.line} color={station.color} className="shrink-0" /><Marquee>{station.name}</Marquee></ItemTitle>
      </ItemContent>
      <ItemActions><ChevronRight className="size-4 text-muted-foreground" /></ItemActions>
    </Item>
  )
}

function LineResultRow({ line }: { line: LineSummary }) {
  const first = line.directions[0]
  return (
    <Item className="pressable" render={<Link to={`/lines/${line.lineCode}`} />}>
      <ItemMedia variant="icon"><Bus className="text-muted-foreground" /></ItemMedia>
      <ItemContent>
        <ItemTitle className="w-full min-w-0 gap-2"><LineBadge code={line.lineCode} className="shrink-0" /><Marquee>{first ? directionLabel(first.from, first.to) : line.lineCode}</Marquee></ItemTitle>
      </ItemContent>
      <ItemActions><ChevronRight className="size-4 text-muted-foreground" /></ItemActions>
    </Item>
  )
}

function StopResultRow({ result }: { result: StopSearchResult }) {
  const { stop } = result
  return (
    <Item className="pressable" render={<Link to={`/stops/${stop.code}`} />}>
      <ItemMedia variant="icon"><MapPin className="text-muted-foreground" /></ItemMedia>
      <ItemContent>
        <ItemTitle><StopName name={stop.name} district={stop.district} /></ItemTitle>
        {stop.towards && <ItemDescription><Towards stop={stop.towards} /></ItemDescription>}
        <LineBadges lines={result.lines} />
      </ItemContent>
      <ItemActions><ChevronRight className="size-4 text-muted-foreground" /></ItemActions>
    </Item>
  )
}
