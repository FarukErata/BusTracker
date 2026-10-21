import { useDeferredValue, useState, type FocusEvent } from 'react'
import { Link } from 'react-router'
import { ChevronRight, History, MapPin, Search, Smartphone, UserRound, X } from 'lucide-react'
import { useAnnouncements, useFavorites, useLineSearch, useStopSearch } from '@/api/hooks'
import type { FavoritesResponse, LineSummary, StopSearchResult } from '@/api/types'
import { EmptyState, ErrorState, LanguageMenu, LineBadge, LineBadges, RowsSkeleton, SectionTitle, StopName, ThemeToggle, Towards } from '@/components/bits'
import { Announcements } from '@/components/planned'
import { Button, buttonVariants } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemMedia, ItemTitle } from '@/components/ui/item'
import { directionLabel, titleCase } from '@/lib/format'
import { useT } from '@/lib/i18n'
import { clearRecents, useRecents, type Recent } from '@/lib/recents'

export function Home() {
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
  const announcements = useAnnouncements(
    favorites.data?.lines.map(l => l.favorite.lineCode) ?? [],
    favorites.data?.stops.map(s => s.favorite.stopCode) ?? [],
  )
  const t = useT()
  if (favorites.isPending) return <RowsSkeleton />
  if (favorites.isError) return <ErrorState message={favorites.error.message} onRetry={() => favorites.refetch()} />

  const { stops, lines } = favorites.data
  if (stops.length === 0 && lines.length === 0)
    return <EmptyState title={t.home.emptyTitle} description={t.home.emptyText} />

  const notices = announcements.data?.announcements ?? []
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
  const lineResults = lines.data ?? []
  const stopResults = query.trim().length >= 2 ? stops.data ?? [] : []
  const loading = (lines.isPending && lines.fetchStatus !== 'idle') || (stops.isPending && stops.fetchStatus !== 'idle')
  const t = useT()

  if (loading && lineResults.length === 0 && stopResults.length === 0) return <RowsSkeleton rows={3} />
  if (lines.isError || stops.isError)
    return <ErrorState message={t.home.searchDown} onRetry={() => { lines.refetch(); stops.refetch() }} />
  if (lineResults.length === 0 && stopResults.length === 0)
    return <EmptyState title={t.home.nothingFound} description={t.home.nothingFoundText} />

  return (
    <>
      {lineResults.length > 0 && <SectionTitle>{t.home.lines}</SectionTitle>}
      <ItemGroup>{lineResults.map(l => <LineResultRow key={l.lineCode} line={l} />)}</ItemGroup>
      {stopResults.length > 0 && <SectionTitle>{t.home.stops}</SectionTitle>}
      <ItemGroup>{stopResults.map(s => <StopResultRow key={s.stop.code} result={s} />)}</ItemGroup>
    </>
  )
}

function LineResultRow({ line }: { line: LineSummary }) {
  const first = line.directions[0]
  return (
    <Item className="pressable" render={<Link to={`/lines/${line.lineCode}`} />}>
      <ItemMedia><LineBadge code={line.lineCode} /></ItemMedia>
      <ItemContent>
        <ItemTitle>{first ? directionLabel(first.from, first.to) : line.lineCode}</ItemTitle>
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
