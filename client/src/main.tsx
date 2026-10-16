import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createBrowserRouter, RouterProvider } from 'react-router'
import 'leaflet/dist/leaflet.css'
import './index.css'
import { rememberInviteFromUrl } from '@/api/client'
import { applyTheme } from '@/lib/theme'
import { AndroidApp } from '@/screens/AndroidApp'
import { Home } from '@/screens/Home'
import { Line } from '@/screens/Line'
import { Stop } from '@/screens/Stop'
import { AccessGate } from '@/screens/Welcome'

rememberInviteFromUrl()
applyTheme()

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: true } },
})

const router = createBrowserRouter([
  {
    element: <AccessGate />,
    children: [
      { path: '/', element: <Home /> },
      { path: '/stops/:code', element: <Stop /> },
      { path: '/lines/:code', element: <Line /> },
    ],
  },
  { path: '/android', element: <AndroidApp /> },
])

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
)
