import { useEffect, useRef, useState } from 'react'
import L from 'leaflet'
import { AttributionControl, CircleMarker, MapContainer, Marker, Polyline, TileLayer, Tooltip, useMap } from 'react-leaflet'
import type { LineStop, TrackedBusView } from '@/api/types'
import type { TrackingView } from '@/lib/tracking'
import { reportAge, secondsSince, titleCase } from '@/lib/format'

const ATTRIBUTION = '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
const ISTANBUL: [number, number] = [41.015, 28.979]
const STOP_DOT_RADIUS = 5
const STOP_PIN_RADIUS = 8

function ThemedTiles() {
  return (
    <>
      <TileLayer url="https://tile.openstreetmap.org/{z}/{x}/{y}.png" attribution={ATTRIBUTION} maxZoom={19} className="map-tiles" />
      <AttributionControl prefix={false} position="bottomright" />
    </>
  )
}

function FitOnce({ points, fitId, bottomInset }: { points: [number, number][]; fitId: string; bottomInset: number }) {
  const map = useMap()
  const fittedFor = useRef<string | null>(fitId)
  useEffect(() => {
    if (points.length === 0 || fittedFor.current === fitId) return
    fittedFor.current = fitId
    map.invalidateSize()
    map.fitBounds(L.latLngBounds(points), { paddingTopLeft: [24, 72], paddingBottomRight: [24, bottomInset + 24], animate: false })
  }, [points, fitId, bottomInset, map])
  return null
}

function InitialFit({ bounds, bottomInset }: { bounds: L.LatLngBounds; bottomInset: number }) {
  const map = useMap()
  useEffect(() => {
    const frame = requestAnimationFrame(() => {
      map.invalidateSize()
      map.fitBounds(bounds, { paddingTopLeft: [32, 80], paddingBottomRight: [32, bottomInset + 48], maxZoom: 16, animate: false })
    })
    return () => cancelAnimationFrame(frame)
  }, [map, bounds, bottomInset])
  return null
}

function KeepInView({ lat, lng, bottomInset, topInset = 72, edge = 48 }: {
  lat: number | undefined; lng: number | undefined; bottomInset: number; topInset?: number; edge?: number
}) {
  const map = useMap()
  useEffect(() => {
    if (lat == null || lng == null) return
    const size = map.getSize()
    const point = map.latLngToContainerPoint([lat, lng])
    const top = topInset + edge
    const bottom = size.y - bottomInset - edge
    const inside = point.x >= edge && point.x <= size.x - edge && point.y >= top && point.y <= bottom
    if (inside) return
    map.panBy([point.x - size.x / 2, point.y - (top + bottom) / 2], { animate: true, duration: 0.3 })
  }, [lat, lng, bottomInset, topInset, edge, map])
  return null
}

export function MapSurface({ className }: { className?: string }) {
  return <div className={`map-surface ${className ?? 'size-full'}`} />
}

function StopFocus({ lat, lng, tracking }: { lat: number; lng: number; tracking: TrackingView | null }) {
  const map = useMap()
  const trackedDoor = tracking?.bus.doorNo ?? null
  const latest = useRef(tracking)
  useEffect(() => {
    latest.current = tracking
  })
  useEffect(() => {
    map.invalidateSize()
    const current = latest.current
    if (!trackedDoor || !current) {
      map.setView([lat, lng], 17, { animate: false })
      return
    }
    const bounds = L.latLngBounds([...current.path, [lat, lng]])
    map.fitBounds(bounds, { padding: [28, 28], maxZoom: 17, animate: false })
  }, [trackedDoor, lat, lng, map])
  return null
}

export function StopMap({ lat, lng, className, tracking = null, inline = true }: {
  lat: number; lng: number; className?: string; tracking?: TrackingView | null; inline?: boolean
}) {
  return (
    <MapContainer
      center={[lat, lng]}
      zoom={17}
      zoomControl={!inline}
      scrollWheelZoom={!inline}
      fadeAnimation={false}
      attributionControl={false}
      className={className ?? 'h-40 w-full'}
    >
      <ThemedTiles />
      <StopFocus lat={lat} lng={lng} tracking={tracking} />
      {tracking && (
        <>
          <Polyline positions={tracking.path} className="route-line" pathOptions={{ weight: 5 }} />
          {tracking.remaining.slice(0, -1).map(stop => (
            <CircleMarker key={stop.code} center={[stop.lat, stop.lng]} radius={STOP_DOT_RADIUS} className="stop-dot">
              <Tooltip direction="top" offset={[0, -6]}>{titleCase(stop.name)}</Tooltip>
            </CircleMarker>
          ))}
          <Marker position={[tracking.bus.lat, tracking.bus.lng]} icon={busIcon(tracking.bus, true)} zIndexOffset={1000} />
          <KeepInView lat={tracking.bus.lat} lng={tracking.bus.lng} bottomInset={0} topInset={0} edge={20} />
        </>
      )}
      <CircleMarker center={[lat, lng]} radius={STOP_PIN_RADIUS} className="stop-pin" />
    </MapContainer>
  )
}

function busIcon(bus: TrackedBusView, selected: boolean) {
  const faded = reportAge(secondsSince(bus.reportedAt)) !== 'fresh'
  const classes = ['bus-marker', `bus-${bus.status}`, bus.variant !== 'D0' ? 'bus-alt' : '', faded ? 'bus-faded' : '', selected ? 'bus-selected' : ''].join(' ')
  return L.divIcon({ className: '', html: `<span class="${classes}">${bus.doorNo}</span>`, iconSize: [0, 0], iconAnchor: [0, 0] })
}

export interface LineMapProps {
  route: [number, number][]
  stops: LineStop[]
  buses: TrackedBusView[]
  pinnedStop: string | null
  selectedBus: string | null
  onStopClick: (code: string) => void
  onBusClick: (doorNo: string) => void
  bottomInset: number
  fitId: string
  ready: boolean
  controls?: boolean
  highlight?: [number, number][]
  busRoute?: [number, number][]
}

function initialBounds(route: [number, number][], stops: LineStop[], buses: TrackedBusView[], pinnedStop: string | null, selectedBus: string | null) {
  const pin = stops.find(s => s.code === pinnedStop)
  const bus = buses.find(b => b.doorNo === selectedBus)
  const focus: [number, number][] = [...(pin ? [[pin.lat, pin.lng] as [number, number]] : []), ...(bus ? [[bus.lat, bus.lng] as [number, number]] : [])]
  return L.latLngBounds(focus.length > 0 ? focus : route.length > 0 ? route : [ISTANBUL])
}

export function LineMap({ route, stops, buses, pinnedStop, selectedBus, onStopClick, onBusClick, bottomInset, fitId, ready, controls = false, highlight, busRoute }: LineMapProps) {
  const selected = buses.find(b => b.doorNo === selectedBus)
  const [initial, setInitial] = useState<{ bounds: L.LatLngBounds; inset: number } | null>(null)
  if (ready && !initial) setInitial({ bounds: initialBounds(route, stops, buses, pinnedStop, selectedBus), inset: bottomInset })

  if (!initial) return <MapSurface />

  return (
    <MapContainer
      bounds={initial.bounds}
      boundsOptions={{ paddingTopLeft: [32, 80], paddingBottomRight: [32, initial.inset + 48], maxZoom: 16 }}
      zoomControl={controls}
      fadeAnimation={false}
      attributionControl={false}
      className="size-full"
    >
      <ThemedTiles />
      <InitialFit bounds={initial.bounds} bottomInset={initial.inset} />
      <FitOnce points={route} fitId={fitId} bottomInset={bottomInset} />
      <KeepInView lat={selected?.lat} lng={selected?.lng} bottomInset={bottomInset} />
      <Polyline positions={route} className="route-line" pathOptions={{ weight: 5 }} />
      {busRoute && <Polyline positions={busRoute} className="route-variant" pathOptions={{ weight: 4, dashArray: '8 6' }} />}
      {highlight && <Polyline positions={highlight} className="route-highlight" pathOptions={{ weight: 7 }} />}
      {stops.map(stop => (
        <CircleMarker
          key={stop.code === pinnedStop ? `${stop.code}-pin` : stop.code}
          center={[stop.lat, stop.lng]}
          radius={stop.code === pinnedStop ? STOP_PIN_RADIUS : STOP_DOT_RADIUS}
          className={stop.code === pinnedStop ? 'stop-pin' : 'stop-dot'}
          eventHandlers={{ click: () => onStopClick(stop.code) }}
        >
          <Tooltip direction="top" offset={[0, -6]}>{stop.seq}. {titleCase(stop.name)}</Tooltip>
        </CircleMarker>
      ))}
      {buses.map(bus => (
        <Marker
          key={bus.doorNo}
          position={[bus.lat, bus.lng]}
          icon={busIcon(bus, bus.doorNo === selectedBus)}
          eventHandlers={{ click: () => onBusClick(bus.doorNo) }}
          zIndexOffset={bus.doorNo === selectedBus ? 1000 : 0}
        />
      ))}
    </MapContainer>
  )
}
