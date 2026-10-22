import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type {
  BusPathResponse,
  DayType,
  FavoritesResponse,
  LineArrivalsResponse,
  LineLiveResponse,
  LineShapesResponse,
  LineStopsResponse,
  LineSummary,
  LinkCodeResponse,
  MeResponse,
  StopArrivalsView,
  StopDetails,
  RailStationResponse,
  RailStationView,
  StopSearchResult,
  TimetableResponse,
} from './types'

const LIVE_REFRESH_MS = 30_000
const STATIC_STALE_MS = 60 * 60_000
const enc = encodeURIComponent

export function useFavorites() {
  return useQuery({ queryKey: ['favorites'], queryFn: () => api<FavoritesResponse>('/api/favorites') })
}

export function useStopSearch(query: string) {
  const q = query.trim()
  return useQuery({
    queryKey: ['search', 'stops', q],
    queryFn: () => api<StopSearchResult[]>(`/api/stops?q=${enc(q)}`),
    enabled: q.length >= 2,
    placeholderData: keepPreviousData,
    staleTime: STATIC_STALE_MS,
  })
}

export function useLineSearch(query: string) {
  const q = query.trim()
  return useQuery({
    queryKey: ['search', 'lines', q],
    queryFn: () => api<LineSummary[]>(`/api/lines?q=${enc(q)}`),
    enabled: q.length >= 1,
    placeholderData: keepPreviousData,
    staleTime: STATIC_STALE_MS,
  })
}

export function useStop(code: string) {
  return useQuery({
    queryKey: ['stop', code],
    queryFn: () => api<StopDetails>(`/api/stops/${enc(code)}`),
    staleTime: STATIC_STALE_MS,
  })
}

const WAITING_RETRY_MIN_MS = 5_000

function stopRefreshMs(data: StopArrivalsView | undefined): number {
  const waiting = data?.lines.filter(l => l.error) ?? []
  if (waiting.length === 0) return LIVE_REFRESH_MS
  const soonest = Math.min(...waiting.map(l => (l.retryInSeconds ?? 0) * 1000))
  return Math.min(LIVE_REFRESH_MS, Math.max(WAITING_RETRY_MIN_MS, soonest))
}

export function useStopArrivals(code: string, lines: string[] = []) {
  const wanted = [...lines].sort().join(',')
  return useQuery({
    queryKey: ['stop', code, 'arrivals', wanted],
    queryFn: () => api<StopArrivalsView>(`/api/stops/${enc(code)}/arrivals${wanted ? `?lines=${enc(wanted)}` : ''}`),
    refetchInterval: query => stopRefreshMs(query.state.data),
    placeholderData: keepPreviousData,
  })
}

export function useLineStops(line: string, enabled = true) {
  return useQuery({
    queryKey: ['line', line, 'stops'],
    queryFn: () => api<LineStopsResponse>(`/api/lines/${enc(line)}/stops`),
    enabled: enabled && !!line,
    staleTime: STATIC_STALE_MS,
  })
}

export function useLineShapes(line: string, enabled = true) {
  return useQuery({
    queryKey: ['line', line, 'shapes'],
    queryFn: () => api<LineShapesResponse>(`/api/lines/${enc(line)}/shapes?all=true`),
    enabled: enabled && !!line,
    staleTime: STATIC_STALE_MS,
  })
}

export function useLineLive(line: string, enabled = true) {
  return useQuery({
    queryKey: ['line', line, 'live'],
    queryFn: () => api<LineLiveResponse>(`/api/lines/${enc(line)}/live`),
    enabled: enabled && !!line,
    refetchInterval: LIVE_REFRESH_MS,
  })
}

export function useLineArrivals(line: string, stop: string | null, direction: string | null) {
  return useQuery({
    queryKey: ['line', line, 'arrivals', stop, direction],
    queryFn: () => api<LineArrivalsResponse>(`/api/lines/${enc(line)}/arrivals?stop=${enc(stop!)}&dir=${enc(direction!)}`),
    enabled: !!stop && !!direction,
    refetchInterval: LIVE_REFRESH_MS,
  })
}

export function useBusPath(line: string | null, bus: string | null, stop: string | null) {
  return useQuery({
    queryKey: ['line', line, 'path', bus, stop],
    queryFn: () => api<BusPathResponse>(`/api/lines/${enc(line!)}/path?bus=${enc(bus!)}&stop=${enc(stop!)}`),
    enabled: !!line && !!bus && !!stop,
    refetchInterval: LIVE_REFRESH_MS,
    placeholderData: keepPreviousData,
  })
}

export function useLineTimetable(line: string, day: DayType | null) {
  return useQuery({
    queryKey: ['line', line, 'timetable', day],
    queryFn: () => api<TimetableResponse>(`/api/lines/${enc(line)}/timetable${day ? `?day=${day}` : ''}`),
    enabled: !!line,
    staleTime: 5 * 60_000,
    placeholderData: keepPreviousData,
  })
}

export function useRailSearch(query: string) {
  const q = query.trim()
  return useQuery({
    queryKey: ['search', 'rail', q],
    queryFn: () => api<RailStationView[]>(`/api/rail/stations?q=${enc(q)}`),
    enabled: q.length >= 2,
    placeholderData: keepPreviousData,
    staleTime: STATIC_STALE_MS,
  })
}

export function useRailStations() {
  return useQuery({
    queryKey: ['rail', 'stations'],
    queryFn: () => api<RailStationView[]>('/api/rail/stations/all'),
    staleTime: STATIC_STALE_MS,
  })
}

export function useRailStation(id: string) {
  return useQuery({
    queryKey: ['rail', 'station', id],
    queryFn: () => api<RailStationResponse>(`/api/rail/stations/${enc(id)}`),
    refetchInterval: LIVE_REFRESH_MS * 2,
  })
}

export function useMe() {
  return useQuery({ queryKey: ['me'], queryFn: () => api<MeResponse>('/api/me') })
}

export function useCreateLinkCode() {
  return useMutation({ mutationFn: () => api<LinkCodeResponse>('/api/me/link-codes', { method: 'POST' }) })
}

export function useRemoveDevice() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => api(`/api/me/devices/${id}`, { method: 'DELETE' }),
    onSettled: () => client.invalidateQueries({ queryKey: ['me'] }),
  })
}

function useFavoriteMutation<TArgs>(request: (args: TArgs) => Promise<unknown>) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: request,
    onSettled: () => client.invalidateQueries({ queryKey: ['favorites'] }),
  })
}

export function useAddFavoriteStop() {
  return useFavoriteMutation((stop: string) => api('/api/favorites/stops', { method: 'POST', body: JSON.stringify({ stop }) }))
}

export function useRemoveFavoriteStop() {
  return useFavoriteMutation((id: number) => api(`/api/favorites/stops/${id}`, { method: 'DELETE' }))
}

export function useAddFavoriteStation() {
  return useFavoriteMutation((station: number) => api('/api/favorites/stations', { method: 'POST', body: JSON.stringify({ station }) }))
}

export function useRemoveFavoriteStation() {
  return useFavoriteMutation((id: number) => api(`/api/favorites/stations/${id}`, { method: 'DELETE' }))
}

export function useAddFavoriteLine() {
  return useFavoriteMutation((args: { line: string; direction: string | null }) =>
    api('/api/favorites/lines', { method: 'POST', body: JSON.stringify(args) }))
}

export function useRemoveFavoriteLine() {
  return useFavoriteMutation((id: number) => api(`/api/favorites/lines/${id}`, { method: 'DELETE' }))
}
