# AI Lab

## UI build sync

`src/AiLab.Api` serves two different copies of the Angular UI:

- **`http://localhost:4200`** — the live dev server (`npm start` in `src/AiLab.UI`). Rebuilds automatically on every file save; always current.
- **`http://localhost:8765`** (the API's own port) — a separate, pre-built static copy served from `src/AiLab.Api/wwwroot`. This does **not** auto-update; it's a snapshot from whenever `ng build` was last run.

**Rule: after making any change under `src/AiLab.UI`, rebuild the static copy** so `:8765` doesn't keep serving stale UI:

```bash
cd src/AiLab.UI && npm run build
```

This runs `ng build`, which outputs straight to `src/AiLab.Api/wwwroot` (configured in `src/AiLab.UI/angular.json`).
