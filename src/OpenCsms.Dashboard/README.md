# OpenCSMS Operator Dashboard

The operator dashboard: a Vue 3.5 + vue-router + Vite + TypeScript SPA over the CSMS API. The API
serves the built `dist` at the site root (`src/OpenCsms.Api/DashboardHosting.cs`, `Csms:Ui:Path`),
and the suite's browser journeys drive this same bundle through a loopback listener.

## Development

```bash
npm ci
npm run build     # typecheck (vue-tsc) + vite build into dist/
npm run dev       # Vite on http://localhost:5181, proxying /api to a running API on :5126
```

`pwsh eng/build-dashboard.ps1` runs the install and the build the way the suite's gate does. The
`.NET` build never invokes Node; without a build the API answers the dashboard routes with a
"not built" page and everything else keeps working.

## Routes

| Route | Screen |
| --- | --- |
| `/sign-in` | Email + password sign-in against `POST /api/auth/sign-in` (HttpOnly cookie). |
| `/` | The operator's stations, tenant-scoped by the session. |
| `/stations/:stationId` | One station and its sessions, newest first. |
| `/invoices` | The operator's invoices, newest first. |
| `/invoices/:invoiceId` | One invoice's energy, start fee and idle fee. |
| `/tariffs` | The operator's tariffs. |
| `/status` | The public network status page; no account needed. |

Route paths and names are what `DiscoverRoutes` reads from the live Vue Router for page coverage, so
a rename is visible in the report.
