# SmartMoney Frontend Agent

You are an expert frontend engineer working on the **SmartMoney** Vue 3 dashboard — a single-page application that displays NIFTY Smart Money Bias data for traders.

## Tech Stack

- **Framework**: Vue 3 (Composition API, `<script setup lang="ts">`)
- **Build tool**: Vite 7 with `@vitejs/plugin-vue`
- **Language**: TypeScript (strict mode via `vue-tsc`)
- **Styling**: Tailwind CSS v4 with PostCSS; dark-mode class strategy (`dark:` variants)
- **Routing**: Vue Router 4 (`createWebHashHistory`)
- **Data**: Static JSON files served from `public/data/` (no live API in production)
- **Deployment**: GitHub Pages via `frontend/dist/` (base path `/smartmoney/`)

## Repository Layout

```
frontend/
├── src/
│   ├── main.ts              # App entry — mounts Vue app, sets dark/light theme from localStorage
│   ├── App.vue              # Root component — renders <RouterView />
│   ├── router/index.ts      # Single route "/" → Dashboard
│   ├── pages/
│   │   └── Dashboard.vue    # Only page; all dashboard logic lives here
│   ├── services/
│   │   └── api.ts           # Typed fetch helpers + DTO types (MarketTodayResponse, MarketHistoryPoint)
│   └── assets/
│       ├── base.css
│       └── main.css
├── public/
│   └── data/                # Static JSON data dropped here by the backend job
│       ├── market_today.json
│       └── market_history_30.json
├── index.html
├── vite.config.ts           # base: "/smartmoney/", port 5174
├── tailwind.config.js
├── tsconfig.json / tsconfig.app.json / tsconfig.node.json
└── package.json
```

## Key Data Types (`src/services/api.ts`)

```ts
type ParticipantDto = {
  name: string;   // e.g. "FII", "DII", "PRO", "RETAIL"
  bias: number;   // normalised bias score
  label?: string; // e.g. "Strong Bullish"
};

type MarketTodayResponse = {
  index: string;          // e.g. "NIFTY"
  date: string;           // ISO date string
  final_score: number;    // composite bias score (negative = bearish, positive = bullish)
  regime: string;         // e.g. "NORMAL" | "SHOCK"
  shock_score?: number;
  participants: ParticipantDto[];
  explanation?: string;
};

type MarketHistoryPoint = {
  date: string;
  final_score: number;
  regime: string;
};
```

Data is fetched via `api.marketToday()` and `api.marketHistory()`.  
The base URL is controlled by `VITE_JSON_BASE_URL` (defaults to `/data`; production uses `/smartmoney/data`).

## Dashboard.vue — Component Overview

The entire UI lives in `src/pages/Dashboard.vue`. Key sections:

| Section | Description |
|---|---|
| **KPI cards** (4-column grid) | Final Score, Regime, 30D Trend, As Of Date |
| **Headline Signal** | Large score + meter bar (bearish ↔ bullish), regime/shock badges |
| **Explanation** | Rule-based text from `today.explanation` |
| **30-Day Trend** | SVG sparkline built from `history` points |
| **Quick Facts** | Index name, Top Driver participant, dates |
| **Participant Table** | FII / DII / PRO / Retail — bias score, influence bar, label |

### Computed helpers (important for extensions)

- `scoreColorClass` / `scoreBadgeClass` — green ≥ 40, red ≤ −40, grey otherwise
- `regimeBadgeClass` — orange for SHOCK, grey for NORMAL
- `shockBadgeClass` — orange ≥ 25, amber ≥ 10, green otherwise
- `participantRows` — normalises raw participant names into canonical FII/DII/PRO/RETAIL rows
- `points` — SVG polyline string for sparkline
- `scoreMeterWidth` — `min(50, |final_score|/2)%` for the bias meter

### Theme

- Dark mode is the default; toggled via `toggleTheme()` / `setTheme(bool)`
- Preference persisted in `localStorage` under key `"theme"`

## Coding Conventions

1. Use **Composition API** with `<script setup lang="ts">` for all new components.
2. Keep components in `src/pages/` (pages) or `src/components/` (shared, create if needed).
3. Use **Tailwind utility classes** directly in templates; prefer `dark:` variants for theming.
4. All new API shapes must have TypeScript types in `src/services/api.ts`.
5. Add new routes in `src/router/index.ts` using lazy imports (`() => import(...)`).
6. Do **not** introduce a state-management library (e.g. Pinia/Vuex) unless the feature genuinely requires cross-component shared state.
7. Static data files go in `public/data/` and are served as-is at build time.
8. Run `npm run type-check` (via `vue-tsc --build`) before committing TypeScript changes.
9. Run `npm run build` to verify the production bundle is clean.

## Development Commands

```bash
cd frontend
npm install          # install dependencies
npm run dev          # dev server on http://localhost:5174/smartmoney/
npm run build        # type-check + production build → frontend/dist/
npm run type-check   # TypeScript check only
npm run preview      # preview the built dist/
```

## Common Tasks & Guidance

### Adding a new page
1. Create `src/pages/MyPage.vue` with `<script setup lang="ts">`.
2. Add a lazy route in `src/router/index.ts`:
   ```ts
   { path: '/my-page', component: () => import('@/pages/MyPage.vue') }
   ```
3. Link to it with `<RouterLink to="/my-page">`.

### Adding a new KPI card
Add a new `<article>` inside the `.dashboard-kpi-grid` section in `Dashboard.vue`, following the existing pattern (label, value, sub-text).

### Fetching new data
1. Add the JSON file to `public/data/`.
2. Define a TypeScript type in `src/services/api.ts`.
3. Export a new function from the `api` object, e.g.:
   ```ts
   export const api = {
     ...
     newEndpoint: () => jsonGet<MyType>("my_file.json"),
   };
   ```

### Styling guidelines
- Use `clamp()` CSS custom properties (see `.dashboard-shell` in `Dashboard.vue`) for responsive sizing.
- All cards use `rounded-xl border border-gray-200 bg-white shadow-sm dark:border-gray-800 dark:bg-gray-900`.
- Color scale: green = bullish/positive, red = bearish/negative, gray = neutral.

## CI / Deployment

The GitHub Actions workflow (`.github/workflows/pages.yml`) runs on weekdays at 9:40 PM IST:
1. Builds the Vue frontend (`npm run build`).
2. Runs the .NET backend job to produce fresh JSON data files.
3. Uploads `frontend/dist/` to GitHub Pages.

The deployed app is available at `https://dotnetdev50.github.io/smartmoney/`.
