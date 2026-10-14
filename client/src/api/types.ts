export type Direction = string

export interface Freshness {
  fetchedAt: string
  ageSeconds: number
  stale: boolean
  staleReason: string | null
  retryAt: string | null
}

export interface StopInfo {
  code: string
  name: string
  district: string
  lat: number
  lng: number
  towards: string | null
}

export interface StopLine {
  lineCode: string
  direction: Direction
  directionName: string
  seq: number
}

export interface StopSearchResult {
  stop: StopInfo
  lines: string[]
}

export interface LineEnds {
  direction: Direction
  from: string
  to: string
}

export interface LineSummary {
  lineCode: string
  directions: LineEnds[]
}

export interface StopDetails {
  stop: StopInfo
  lines: StopLine[]
}

export interface ArrivalView {
  doorNo: string
  routeCode: string
  variant: string
  stopsAway: number
  distanceMeters: number
  etaSeconds: number | null
  notDeparted: boolean
  ageSeconds: number
  lastStopSeq: number | null
  lastStopName: string | null
}

export interface PassedView {
  doorNo: string
  routeCode: string
  variant: string
  metersPast: number
  lastStopSeq: number | null
}

export interface StopArrivalView {
  line: string
  direction: Direction
  directionName: string
  bus: ArrivalView
}

export interface LineStatusView {
  line: string
  direction: Direction
  directionName: string
  data: Freshness | null
  error: string | null
}

export interface StopArrivalsView {
  stop: StopInfo
  arrivals: StopArrivalView[]
  lines: LineStatusView[]
}

export interface LineStop {
  seq: number
  code: string
  name: string
  lat: number
  lng: number
  district: string
}

export interface LineDirection {
  direction: Direction
  name: string
  stops: LineStop[]
}

export interface LineStopsResponse {
  line: string
  directions: LineDirection[]
}

export interface ShapeView {
  routeCode: string
  direction: Direction
  variant: string
  lengthMeters: number
  durationSeconds: number
  points: [number, number][]
}

export interface LineShapesResponse {
  line: string
  shapes: ShapeView[]
}

export type BusStatus = 'ok' | 'at-terminal' | 'arrived' | 'stale' | 'off-route' | 'no-route'

export interface TrackedBusView {
  doorNo: string
  lat: number
  lng: number
  direction: Direction
  variant: string
  routeCode: string
  destination: string
  reportedAt: string
  ageSeconds: number
  status: BusStatus
  tripId: string
  onOwnVariant: boolean
  alongMeters: number | null
  offsetMeters: number | null
  routeLengthMeters: number | null
  lastStopSeq: number | null
  nextStopSeq: number | null
  progressToNext: number | null
  speedKmh: number | null
  passedStopSeqs: number[]
}

export interface LineLiveResponse {
  line: string
  data: Freshness
  buses: TrackedBusView[]
}

export interface LineArrivalsResponse {
  line: string
  direction: Direction
  stop: { code: string; seq: number; name: string }
  data: Freshness
  approaching: ArrivalView[]
  recentlyPassed: PassedView[]
}

export interface FavoriteLine {
  id: number
  lineCode: string
  direction: Direction | null
  label: string | null
  createdAt: string
}

export interface FavoriteStop {
  id: number
  stopCode: string
  stopName: string
  label: string | null
  createdAt: string
}

export interface FavoritesResponse {
  lines: { favorite: FavoriteLine; directions: Record<string, string> }[]
  stops: { favorite: FavoriteStop; stop: StopInfo | null; lines: string[] }[]
}

export interface PathStop {
  seq: number
  code: string
  name: string
  lat: number
  lng: number
}

export interface BusPathResponse {
  line: string
  routeCode: string
  variant: string
  onRoadGeometry: boolean
  bus: TrackedBusView
  stop: PathStop
  passed: boolean
  distanceMeters: number
  path: [number, number][]
  remainingStops: PathStop[]
  data: Freshness
}
