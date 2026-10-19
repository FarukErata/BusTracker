import { useEffect, useState } from 'react'

export function useSecondsUntil(iso: string | number | undefined) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (iso === undefined) return
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [iso])
  if (iso === undefined) return 0
  return ((typeof iso === 'number' ? iso : Date.parse(iso)) - now) / 1000
}
