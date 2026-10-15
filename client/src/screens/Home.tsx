import { useDeferredValue, useState } from 'react'
import { Link } from 'react-router'
import { ChevronRight, MapPin, Search, Smartphone, X } from 'lucide-react'
import { useFavorites, useLineSearch, useStopSearch } from '@/api/hooks'
import type { FavoritesResponse, LineSummary, StopSearchResult } from '@/api/types'
import { EmptyState, ErrorState, LineBadge, LineBadges, RowsSkeleton, SectionTitle, StopName, ThemeToggle, Towards } from '@/components/bits'
import { Button, buttonVariants } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemMedia, ItemTitle } from '@/components/ui/item'
import { directionLabel, titleCase } from '@/lib/format'

export function Home() {
  const [query, setQuery] = useState('')
  const deferred = useDeferredValue(query)
  const searching = query.trim().length > 0

  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-md flex-col">
      <header className="flex items-center gap-2 px-3 pt-3">
        <h1 className="flex-1 text-lg font-semibold tracking-tight">Kaç Kaldı</h1>
        <Link to="/android" className={buttonVariants({ variant: 'ghost', size: 'icon' })} aria-label="Get the Android app">
          <Smartphone />
        </Link>
        <ThemeToggle />
      </header>
      <div className="px-3 pt-2">
        <div className="relative">
          <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
          <Input
            value={query}
            onChange={e => setQuery(e.target.value)}
            placeholder="Stop or line, like Fabrikalar or 500T"
            className="pr-9 pl-8"
            aria-label="Search stops and lines"
          />
          {searching && (
            <Button variant="ghost" size="icon" className="absolute top-1/2 right-0.5 -translate-y-1/2" onClick={() => setQuery('')} aria-label="Clear search">
              <X />
            </Button>
          )}
        </div>
      </div>
      {searching ? <SearchResults query={deferred} /> : <Favorites />}
    </div>
  )
}

function Favorites() {
  const favorites = useFavorites()
  if (favorites.isPending) return <RowsSkeleton />
  if (favorites.isError) return <ErrorState message={favorites.error.message} onRetry={() => favorites.refetch()} />

  const { stops, lines } = favorites.data
  if (stops.length === 0 && lines.length === 0)
    return <EmptyState title="Save the stops and lines you use" description="Search above, open a stop or a line, and tap the star." />

  return (
    <>
      {stops.length > 0 && <SectionTitle>Your stops</SectionTitle>}
      <ItemGroup>{stops.map(s => <FavoriteStopRow key={s.favorite.id} item={s} />)}</ItemGroup>
      {lines.length > 0 && <SectionTitle>Your lines</SectionTitle>}
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
  return (
    <Item className="pressable" render={<Link to={target} />}>
      <ItemMedia><LineBadge code={lineCode} /></ItemMedia>
      <ItemContent>
        <ItemTitle>{label ?? (dir ? titleCase(dir) : 'Both directions')}</ItemTitle>
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

  if (loading && lineResults.length === 0 && stopResults.length === 0) return <RowsSkeleton rows={3} />
  if (lines.isError || stops.isError)
    return <ErrorState message="Search isn't available right now." onRetry={() => { lines.refetch(); stops.refetch() }} />
  if (lineResults.length === 0 && stopResults.length === 0)
    return <EmptyState title="Nothing found" description="Try part of a stop name or a line code." />

  return (
    <>
      {lineResults.length > 0 && <SectionTitle>Lines</SectionTitle>}
      <ItemGroup>{lineResults.map(l => <LineResultRow key={l.lineCode} line={l} />)}</ItemGroup>
      {stopResults.length > 0 && <SectionTitle>Stops</SectionTitle>}
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
