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

export type DayType = 'I' | 'C' | 'P'

export interface PlannedTripView {
  departs: string
  atStop: string | null
  routeCode: string
  variant: string
  cancelled: boolean
  nextDay: boolean
}

export interface PlannedView {
  direction: Direction
  firstStop: string
  dayType: DayType
  holiday: boolean
  next: PlannedTripView[]
  nextDate: string
}

export interface AnnouncementView {
  line: string
  kind: 'cancelled' | 'notice'
  message: string
  postedAt: string
  direction: Direction | null
  fromStop: string | null
  departure: string | null
  reason: string | null
}

export interface TimetableTripView {
  time: string
  at: string
  routeCode: string
  variant: string
  cancelled: boolean
}

export interface TimetableDirectionView {
  direction: Direction
  name: string
  firstStop: string
  trips: TimetableTripView[]
}

export interface TimetableResponse {
  line: string
  dayType: DayType
  holiday: boolean
  todayDayType: DayType
  available: boolean
  directions: TimetableDirectionView[]
  announcements: AnnouncementView[]
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
  heavyTraffic: boolean
  planned: PlannedTripView | null
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
  retryInSeconds: number | null
  planned: PlannedView | null
}

export interface StopArrivalsView {
  stop: StopInfo
  arrivals: StopArrivalView[]
  lines: LineStatusView[]
  announcements: AnnouncementView[]
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
  heavyTraffic: boolean
  planned: PlannedTripView | null
}

export interface LineLiveResponse {
  line: string
  data: Freshness
  buses: TrackedBusView[]
  planned: PlannedView[]
  announcements: AnnouncementView[]
}

export interface LineArrivalsResponse {
  line: string
  direction: Direction
  stop: { code: string; seq: number; name: string }
  data: Freshness
  approaching: ArrivalView[]
  recentlyPassed: PassedView[]
  planned: PlannedView | null
  announcements: AnnouncementView[]
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
  announcements: AnnouncementView[]
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

export interface DeviceView {
  id: number
  platform: string
  name: string
  createdAt: string
  lastSeenAt: string
  current: boolean
}

export interface MeResponse {
  currentDeviceId: number
  devices: DeviceView[]
}

export interface LinkCodeResponse {
  code: string
  expiresAt: string
}
