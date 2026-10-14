import { useBusPath } from '@/api/hooks'
import type { PathStop, TrackedBusView } from '@/api/types'

export interface TrackedSelection {
  line: string
  direction: string
  doorNo: string
}

export interface TrackingView {
  bus: TrackedBusView
  remaining: PathStop[]
  path: [number, number][]
}

export function useTracking(selection: TrackedSelection | null, stopCode: string): TrackingView | null {
  const path = useBusPath(selection?.line ?? null, selection?.doorNo ?? null, selection ? stopCode : null)
  const data = path.data
  if (!selection || !data || data.bus.doorNo !== selection.doorNo) return null
  return { bus: data.bus, remaining: data.remainingStops, path: data.path }
}
