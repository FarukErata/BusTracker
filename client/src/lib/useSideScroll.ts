import { useEffect, useRef } from 'react'

const DRAG_THRESHOLD_PX = 5

export function useSideScroll<T extends HTMLElement>() {
  const ref = useRef<T>(null)

  useEffect(() => {
    const el = ref.current
    if (!el) return

    const onWheel = (e: WheelEvent) => {
      if (Math.abs(e.deltaX) >= Math.abs(e.deltaY)) return
      const max = el.scrollWidth - el.clientWidth
      const atEnd = (e.deltaY < 0 && el.scrollLeft <= 0) || (e.deltaY > 0 && el.scrollLeft >= max - 1)
      if (max <= 0 || atEnd) return
      e.preventDefault()
      el.scrollLeft += e.deltaY
    }

    let startX = 0
    let startScroll = 0
    let pointer: number | null = null
    let dragged = false

    const onDown = (e: PointerEvent) => {
      if (e.pointerType !== 'mouse' || e.button !== 0) return
      pointer = e.pointerId
      startX = e.clientX
      startScroll = el.scrollLeft
      dragged = false
    }
    const onMove = (e: PointerEvent) => {
      if (pointer !== e.pointerId) return
      const dx = e.clientX - startX
      if (!dragged && Math.abs(dx) < DRAG_THRESHOLD_PX) return
      if (!dragged) el.setPointerCapture(e.pointerId)
      dragged = true
      el.scrollLeft = startScroll - dx
    }
    const onUp = (e: PointerEvent) => {
      if (pointer !== e.pointerId) return
      pointer = null
      if (el.hasPointerCapture(e.pointerId)) el.releasePointerCapture(e.pointerId)
    }
    const onClick = (e: MouseEvent) => {
      if (!dragged) return
      e.stopPropagation()
      e.preventDefault()
      dragged = false
    }

    el.addEventListener('wheel', onWheel, { passive: false })
    el.addEventListener('pointerdown', onDown)
    el.addEventListener('pointermove', onMove)
    el.addEventListener('pointerup', onUp)
    el.addEventListener('pointercancel', onUp)
    el.addEventListener('click', onClick, true)
    return () => {
      el.removeEventListener('wheel', onWheel)
      el.removeEventListener('pointerdown', onDown)
      el.removeEventListener('pointermove', onMove)
      el.removeEventListener('pointerup', onUp)
      el.removeEventListener('pointercancel', onUp)
      el.removeEventListener('click', onClick, true)
    }
  }, [])

  return ref
}
