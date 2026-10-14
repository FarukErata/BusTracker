# Buses web client

React + Vite + TypeScript + Tailwind, shadcn/ReUI components on Base UI, Leaflet maps, TanStack Query.

## Develop

Run the API (`dotnet run --project src/BusTracker.Api --launch-profile http`, port 5080), then:

```bash
npm install
npm run dev
```

The app runs at http://127.0.0.1:5173 and proxies `/api` and `/health` to the API.

## Build

`npm run build` writes the app into `src/BusTracker.Api/wwwroot`, which the API serves at `/`. That folder is not committed; build it before `dotnet publish`.

## Notes

- Map tiles are OpenStreetMap's standard tiles; dark mode inverts them with a CSS filter. CARTO basemaps now need an API key.
- Leaflet uses z-index 400+ inside the map, so every map container sits in its own stacking context (`isolate`) to stay under the bottom sheet.
- Each device registers itself on first use (`POST /api/devices`) and keeps its key in local storage. An `?invite=` link parameter is remembered and sent with the registration.
