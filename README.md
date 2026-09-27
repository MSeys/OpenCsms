# OpenCSMS

OpenCSMS is a small but real **EV charging management system** (CSMS) built as the **reference suite
for [ProtoTest](https://github.com/MSeys/ProtoTest)** — not as a sample inside the framework
repository.

It is deliberately a product, not a demo around a feature: a REST API, PostgreSQL, a billing worker on
RabbitMQ, and the tests that prove a charging session becomes an invoice. ProtoTest is the only test
framework. Every framework gap this app pulled is written down here rather than hidden.

## What this proves

The point is the *one suite, many environments* promise, without `if` statements in test setup:

| Mode | API | PostgreSQL & RabbitMQ | State |
| --- | --- | --- | --- |
| In-process + Testcontainers | hosted by the suite (`UseInProcess`) | started by the suite (Testcontainers) | verified: 64/64 green, 0 skipped, fresh containers, seven Chromium journeys included (`artifacts/gates/opencsms-container-20260927-195312.log`) |
| Configured | hosted by the suite | an environment you provide through configuration; the suite's containers skip | verified: 64/64, 0 skipped, one persistent database (`artifacts/gates/opencsms-configured-20260927-195250.log`) |
| Published, locally rehearsed | a real API process at `ProtoTest:Applications:Csms:BaseUrl`; the real `OpenCsms.Billing.Worker` process consumes | running `opencsms-postgres` / `opencsms-rabbitmq` containers addressed by the configured keys | verified: 64 total - 57 passed, 7 clock-dependent journeys skipped, the worker runs once (`artifacts/gates/opencsms-published-20260927-195118.log`); no staging target exists yet |
| Container topology (planned) | a container | containers, `docker compose` / Aspire | M4 |

The suite's `Setup` is the same code in every mode: each target - the store, the broker, the API
with its billing worker, the dashboard application - declares its providers in order, and the first
one the environment makes available serves it. Infrastructure the environment already provides is
not started, and a worker follows its application: hosted with the in-process server, run by the
environment otherwise. The product resolves its broker address on first publish and its database
string when the context is first resolved, so settings that arrive after registration are seen.

## Status

M1 and M2 are done, and M3's dashboard work is done through R3.2: R3.0's layering is in (below),
R3.1 added the dashboard shell, sign-in and roles with the two browser journeys, and R3.2 added
the views' depth (the session timeline with invoice links, the invoice calculation lines, tariff
repricing for the operator admin with an honest viewer UI), the monthly `.xlsx` export asserted
through Sheets, the seeded busy month behind it, and the multi-tenancy and viewer-restriction
journeys. The OCPP 1.6J
gateway, the charge-point simulator's journey surface and the device-fit proof are in (R2.1/R2.2),
including the idle-fee journeys that advance the injected test clock instead of sleeping, and the
error paths — a duplicate StopTransaction, malformed MeterValues, an unknown charge point,
out-of-subset actions and out-of-range connectors — are pinned with the OCPP error each one answers
with (R2.3). The remote
endpoints' failure branches are pinned too (R2.4): an offline charge point is `409`, a device that
refuses the call is `502`, no answer within the configured timeout is `504`, and a device's own
authorization decision (for example `Blocked`) is the `200` body's status, not a failure. The
gateway bills each session from the connector register it started at, so a second session on a
connector invoices its own energy only (R2.3). The suite is green
(`dotnet test tests/OpenCsms.Suite` → 64/64, domain 52/52): the default mode with fresh
Testcontainers and the seven Chromium journeys is recorded at
`artifacts/gates/opencsms-container-20260927-195312.log`, the configured mode against one
persistent database at `artifacts/gates/opencsms-configured-20260927-195250.log` (its seed was
already in place, which is the seeder's idempotency proof), and the published rehearsal at
`artifacts/gates/opencsms-published-20260927-195118.log` — the Setup is the same code in every
mode. The milestones below are the ones in the
[reference demo brief](https://github.com/MSeys/ProtoTest) — this README tracks them honestly.

**Layering (R3.0).** The solution is concentric layers, and a project depends only inward:
`Domain` ← `Application` ← `Infrastructure` / `Protocol.Ocpp` / `Api` / `Worker`, with `Contracts`
on the side.

| Project | Owns |
| --- | --- |
| `src/OpenCsms.Domain` | Aggregates, value objects and invariants; no EF, HTTP or JSON. |
| `src/OpenCsms.Application` | The use cases, over narrow persistence, event and clock ports; no infrastructure. |
| `src/OpenCsms.Contracts` | The integration events and the API's request/response DTOs; no project references. |
| `src/OpenCsms.Infrastructure` | The EF `DbContext` with its fluent configurations, migrations and stores, and the RabbitMQ publisher and topology. |
| `src/OpenCsms.Protocol.Ocpp` | The OCPP 1.6J wire format, translated to application commands. |
| `src/OpenCsms.Api`, `src/OpenCsms.Billing.Worker` | Thin composition roots: they bind requests and deliveries and map answers, status codes and DTOs. |

The sub-stages landed in order, each gated and behavior-preserving: **R3.0a** extracted the
application layer (`d23ec58`), **R3.0b** merged persistence and messaging into infrastructure
(`a910381`), **R3.0c** extracted the protocol and gave the aggregates their operations (`3cafbf7`),
and **R3.0d** moved the API's DTOs to `OpenCsms.Contracts`, with the mapping from the domain left in
the API. Behavior, endpoints, DTO shapes, status codes, events, schema and OCPP semantics are
unchanged; the suite passes unchanged (41/41 twice on fresh containers:
`artifacts/gates/opencsms-container-20260926-220015.log` and
`artifacts/gates/opencsms-container-20260926-220049.log`; domain 48/48).

[COVERAGE.md](COVERAGE.md) records what the suite asserts today and the areas that are still
untested.

- [x] M1 — CSMS API + PostgreSQL + billing worker + REST suite + one journey
- [x] M2 — OCPP gateway + charge-point simulator + idle-fee journey
- [x] M3 — dashboard + monthly export + browser journeys (R3.0a–d: layering; R3.1: shell, roles and browser wiring; R3.2: view depth, tariff repricing, the `.xlsx` export with Sheets assertions, the seeded month, the viewer and multi-tenancy journeys)
- [ ] M4 — container topology + deployed mode + fault injection + nightly CI

## OCPP 1.6J subset

The gateway speaks OCPP 1.6J over one WebSocket per charge point at `/ocpp/{chargePointId}`. The JSON
framing is `[2, id, action, payload]` for a call, `[3, id, payload]` for a call result and
`[4, id, code, description, details]` for a call error — the framing contract lives in
`src/OpenCsms.Protocol.Ocpp`, shared by the gateway and the simulator. This is the whole subset:

| Message | Direction | What the CSMS does | Suite |
| --- | --- | --- | --- |
| `BootNotification` | charge point → CSMS | accepts a registered charge point with the configured heartbeat interval, rejects an unknown identity, marks the station seen | `ChargePointsChargeOverOcpp`, `OcppErrorPaths` |
| `Heartbeat` | charge point → CSMS | answers with the injected clock's time and refreshes the station's last-seen stamp | `ChargePointsChargeOverOcpp`, `OcppErrorPaths` |
| `StatusNotification` | charge point → CSMS | upserts the connector's status (connector `0` is the charge point itself; `0..connectorCount`) | `ChargePointsChargeOverOcpp`, `OcppErrorPaths` |
| `StartTransaction` | charge point → CSMS | opens a charging session at the connector's reported register (the session bills from that baseline), answers with the CSMS-assigned transaction number; a busy connector gets `ConcurrentTx` | `ChargePointsChargeOverOcpp`, `OcppErrorPaths` |
| `MeterValues` | charge point → CSMS | records the last active-energy register sample (Wh or kWh) on the session and bills the part above the session's start reading | `ChargePointsChargeOverOcpp`, `OcppErrorPaths` |
| `StopTransaction` | charge point → CSMS | ends the session, publishes `session.ended`; a stop for an already-ended transaction is answered without processing it twice | `ChargePointsChargeOverOcpp`, `OcppErrorPaths` |
| `RemoteStartTransaction` | CSMS → charge point | `POST /api/stations/{id}/remote-start` sends the call and waits for the device's own answer under the configured remote timeout | `TheOperatorRemotelyStartsAndStopsAChargePoint`; `ChargePointsChargeOverOcpp` pins the failure branches |
| `RemoteStopTransaction` | CSMS → charge point | `POST /api/sessions/{id}/remote-stop` sends the call and waits the same way | `TheOperatorRemotelyStartsAndStopsAChargePoint`; `ChargePointsChargeOverOcpp` pins the failure branches |

Everything else (Authorize, DataTransfer, Reset, UnlockConnector, UpdateFirmware, the firmware and
diagnostics status notifications, smart-charging profiles) is outside the subset and refused with a
call error, and `OcppErrorPaths` pins each code: `NotImplemented` for a named action,
`FormationViolation` for a frame the subset cannot read (a non-numeric energy sample, a sample without
the energy measurand), `PropertyConstraintViolation` for a value outside its range (a connector the
station does not have, a reading below the session's last one), and `InternalError` for a call from a
charge point no station is registered for — its `BootNotification` is answered `Rejected`. The suite's
charge point is `AcCharger`, built on `ProtoTest.Devices.WebSocket`: the same registration reaches the
in-process gateway through its `TestServer` and a real endpoint over a socket, with no mode
conditionals.

The two remote endpoints answer a failed call honestly: a charge point that is not connected is
`409 Conflict` (both endpoints), a device that refuses the call with an OCPP call error is
`502 Bad Gateway`, and one that takes the call but does not answer within `Ocpp:RemoteCallTimeoutSeconds`
(ten seconds by default) is `504 Gateway Timeout`. A device's own authorization decision — for example
`Blocked` — is not a failure: the operator reads it from the `200` body's `status`, exactly as the
happy path reads `Accepted`. `ChargePointsChargeOverOcpp` pins all four branches; the journey that
waits for a `504` reads the API's effective timeout — two seconds while the suite hosts the test
server, the environment's own `Ocpp:RemoteCallTimeoutSeconds` against a running stack — instead of
assuming either.

## Operator dashboard (R3.1)

The operator dashboard is a Vue 3.5 + vue-router + Vite SPA in `src/OpenCsms.Dashboard`, served by
the API at the site root from `Csms:Ui:Path` (default `../OpenCsms.Dashboard/dist`, relative to the
API's content root) with a static-files pass and an SPA fallback: `/api`, `/ocpp`, `/healthz` and
`/swagger` keep their namespaces, so an unmatched path under them is a 404, never a page. Without a
build the dashboard routes answer a "not built" page and everything else keeps working.
`eng/build-dashboard.ps1` runs `npm ci` + `npm run build` (vue-tsc + vite); `eng/run-suite.ps1`
builds it before every gate run, so the browser journeys always test a fresh bundle.

Routes the SPA ships: `/sign-in`, `/` (stations), `/stations/:stationId` (sessions),
`/invoices`, `/invoices/:invoiceId`, `/tariffs`, `/status` (public). The suite's web session
declares `DiscoverRoutes`, so page coverage comes from the live Vue Router.

**Sign-in and roles.** `POST /api/auth/sign-in` exchanges a tenant-scoped account's email and
password (PBKDF2-SHA256 hashes in the `Users` table) for an HttpOnly cookie; `GET /api/auth/session`
answers the SPA, `POST /api/auth/sign-out` clears it. A user is `operator` (admin) or `viewer`.
`POST /api/users` provisions an account; until M4 gives the management API credentials of its own it
sits on the same unauthenticated surface as `/api/tariffs` and `/api/stations`, and the suite
provisions every test's operator through it — the routes stay the front door, with the mechanics
on `ProtoTest.Data` provisioners (`tests/OpenCsms.Suite/Support/CsmsProvisioners.cs`, orchestrated
by `CsmsProvisioning` for `CsmsOperatorAttribute`, the neighbor tenants, the viewers and the
contract tests). Provisioned rows stay, because the API has no delete route; unique names keep
reruns against a database that outlives the test process independent.

**The read surface.** `/api/dashboard/{stations,stations/{id},stations/{id}/sessions,invoices,invoices/{id},tariffs}`
requires a signed-in session and scopes every query to the session's tenant claim — a tenant is
never taken from the request. `/api/status/stations` is the public page: station names, OCPP
identities, last-seen stamps and connector statuses, no tenant or pricing detail. The
operator commands are mirrored for the dashboard under `/api/dashboard` and require the `operator`
role: a viewer gets `403` before anything reaches a charge point, and the station is scoped to the
session's tenant first, so another tenant's station answers `404` exactly like an unknown one
(`TenantIsolation` pins both directions). The machine API's
`/api/stations/{id}/remote-start` and `/api/sessions/{id}/remote-stop` are unchanged.

**The views' depth (R3.2).** The station screen is the session timeline — connector, start/end,
energy, state and the invoice link for billed sessions (matched client-side from the invoice
list) — and the operator admin gets the remote-start panel on it; viewers never see the panel.
The invoice screen shows the worker's calculation lines (energy, start fee, idle fee, total).
Tariff repricing is the operator admin's form on the tariffs screen
(`PUT /api/dashboard/tariffs/{id}`, `operator` role, tenant-scoped `404`, negative prices `400`);
viewers read the table with a read-only note instead. Stored invoices keep the prices they were
billed at — only new sessions bill at the new ones (`TariffEditingContracts` bills one session
each side of a repricing to prove it). Cookie mutations carry the anti-forgery token the SPA
fetches from `GET /api/auth/xsrf` as `X-XSRF-TOKEN`; without one the API answers `400`.

| Screen | operator admin | viewer |
| --- | --- | --- |
| Stations, station timeline, invoices, invoice lines, tariffs, public status | reads | reads |
| Remote start/stop, tariff repricing, monthly download | acts (token + role) | hidden in the UI, `403` at the API |

**The monthly export (R3.2).** `GET /api/invoices/export?month=YYYY-MM` answers the tenant's
month as a real `.xlsx` download (`invoices-YYYY-MM.xlsx`): an `Invoices` sheet with one row per
stored invoice issued inside the UTC month (oldest first) and a `Summary` sheet with the month,
the row count and the summed total. The composition lives in the `MonthlyInvoiceExport`
application use case over the invoice queries; the spreadsheet mechanics live in Infrastructure
(`ClosedXmlInvoiceExportWriter`, behind the `IInvoiceExportWriter` port), because the workbook
library is an infrastructure detail the application never sees. ClosedXML was chosen for its
small surface and its MIT license — EPPlus's noncommercial license does not fit this MIT
repository, and raw OpenXML is needlessly verbose for a two-sheet table. Totals are written as
literal values, never formulas, so the file carries exactly what the invoice rows hold. A missing
or malformed month is `400`, anonymous reads are `401`, and the numbers always match the invoice
rows — the journey sums both sides.

**The seeded busy month (R3.2).** The suite seeds volume once per run
(`Support/SeededMonth.cs`, an `AddRunSetup` step after the containers): two tenants sharing May
2030, each with a tariff, a station, an operator admin and 120 billed sessions (5–25 kWh, no idle
fee), composed from the product's own application services in-process against the run's database.
The worker and the broker are not involved — issuance runs with its events dropped. The seeded
tariff's name is the idempotency marker: a rerun against a database that already holds it stores
nothing, so configured-mode reruns share one seed. Per-test prerequisites still go through the
REST front door — the same routes, with the mechanics on `ProtoTest.Data` provisioners; the seed
is volume only.

**How the suite drives the browser.** The `Dashboard` application is the same API served by a
loopback listener inside the test process when no environment address is configured
(`UseConfigured().UseLoopback(CsmsApi.Create)` from `ProtoTest.AspNetCore`, the documented
api-then-browser recipe: the suite passes the API's `Create` method and the run's collected
configuration arrives as command-line arguments) and publishes the address it bound as its
`BaseUrl`; the readiness probe waits on the real `/healthz`, and the browser session resolves
that one address. The `Csms` application is the REST and OCPP server - the in-process test server
in the default mode, the configured address otherwise - so provisioning and the device journeys
follow the same chain. The loopback's configuration arrives as arguments because a hand-built
`WebApplication` has no initializer forwarding it. One browser journey needs more: the remote-stop
click test drives a connected charge point through the dashboard's own remote command, and the
loopback instance never sees the in-process journeys' connections, so a second device client hangs
off `Dashboard` and reaches it over a real socket - the in-process client stays the clock-true path
the OCPP journeys assert against.

## Gap log

This is the honest state: M1, M2 and M3's dashboard work are done, and the reference-suite
audit's fixes (R1a) landed before new feature work.

- **Ending a session is at-most-once.** `/api/sessions/{id}/end` commits the ended session before it
  publishes `session.ended`; if the publish fails the client gets a 500 but the session stays ended and
  the event is never published, so a retry answers 409 and the invoice is lost. M1 accepts this for now;
  the fix (a transactional outbox or a retry) is scheduled with M4's fault-injection work.
- **Every test connects to the broker at setup.** The run-wide `Tap(CsmsEvents.Exchange)` pre-bind
  makes the suite's tap prepare its destination during test setup, including the REST-only contract
  tests, so a run without a broker fails setup there rather than skipping; the broker capability is
  what gates the tests that need it, and the pre-bind is suite-wide by design (R1a-13). Recorded in
  [COVERAGE.md](COVERAGE.md).
- The DLQ assertion uses a raw RabbitMQ client (`tests/OpenCsms.Suite/Support/RabbitMqRawClient.cs`):
  ProtoTest's tap binds destinations as exchanges and `ProtoMessage` drops the routing key, so a queue
  cannot be awaited through it. The `(exchange, routingKey)` addition is recorded in plan-5 (REF-5) for
  a future release; the helper carries the assertion until then.
- **A browser download is named binary content.** `WebDownload` implements `IProtoBinaryContent`
  (fixed in the framework during M2), so the invoices screen's download journey opens the captured
  export in one line.
- Container topology is planned (M4.5) and payments are not built. Published mode is rehearsed
  locally (the mode table above) but has no staging target yet; the rest of OCPP beyond the named
  refusals, and an operator remote start racing a reconnect, have no tests yet —
  [COVERAGE.md](COVERAGE.md) lists the untested surface.
- **The dashboard's views are done through R3.4.** The timeline, invoice lines, tariff repricing,
  the export with its Sheets assertions, the viewer journey and the multi-tenancy negative test
  are in; the invoices screen's download button is clicked in Chromium with the downloaded bytes
  asserted as real cells, and the remote-stop button drives a connected charge point from the
  station screen.
- **The export speaks the invoice API's names.** The `Invoices` sheet headers are `IssuedAtUtc`,
  `StartFeeAmount` and `IdleFeeAmount`, exactly as `InvoiceResponse` names them; the per-row
  journey fails on a swapped or renamed column.
- **The machine management API is unauthenticated until M4.** `/api/tariffs`, `/api/stations`,
  `/api/sessions`, `/api/users` and the machine remote commands are the device/operator surface
  with no credentials yet; the dashboard's own reads, its tariff repricing, its remote commands
  and the export are cookie-authenticated, tenant-scoped and (for the mutations) role-checked.
  The cookie is `SameSite=Lax`; the dashboard's mutations carry anti-forgery tokens since R3.2.

## Running

```bash
dotnet test
```

On a machine with a container runtime the suite starts PostgreSQL and RabbitMQ itself. Without one,
provide the environment through configuration; all three declared keys must be set for the suite's
containers to skip:

```bash
export ConnectionStrings__Csms="Host=localhost;Database=opencsms;Username=opencsms;Password=opencsms"
export Messaging__RabbitMq__ConnectionString="amqp://guest:guest@localhost:5672"
export ProtoTest__Messaging__RabbitMq__ConnectionString="amqp://guest:guest@localhost:5672"
dotnet test
```

The first key is the product's database, the second the product's broker, and the third the address
ProtoTest's own messaging tap uses.

Each mode has a recorded run: `pwsh eng/run-suite.ps1 -Mode container` and
`pwsh eng/run-suite.ps1 -Mode configured` (the latter requires the three keys above) stream the run to
the console and tee it to `artifacts/gates/opencsms-<mode>-<timestamp>.log`. Both modes build the
dashboard first (`eng/build-dashboard.ps1`: `npm ci` + `npm run build`), so the browser journeys
always test a fresh bundle; run it alone to build without running the suite.

A published rehearsal points the same suite at a running stack: start the API and the Billing worker
as real processes (`dotnet src/OpenCsms.Api/bin/Release/net8.0/OpenCsms.Api.dll --urls
http://127.0.0.1:5080 --contentRoot src/OpenCsms.Api` and `dotnet
src/OpenCsms.Billing.Worker/bin/Release/net8.0/OpenCsms.Billing.Worker.dll`), export the five keys —
`ConnectionStrings__Csms`, `Messaging__RabbitMq__ConnectionString`,
`ProtoTest__Messaging__RabbitMq__ConnectionString`, `ProtoTest__Applications__Csms__BaseUrl` and
`ProtoTest__Applications__Dashboard__BaseUrl` — and run `dotnet test tests/OpenCsms.Suite -c Release`.
The suite's provider chains step aside, the environment runs its own worker and clock, and the
clock-dependent journeys skip; the recorded run is under
`artifacts/gates/opencsms-published-<timestamp>.log`.

`docker compose up` (M4) starts the whole topology for manual use.

## Packages

This repository consumes ProtoTest packages. While 1.1 is under development it resolves them from a
local feed produced by `eng/pack.ps1` in a sibling ProtoTest checkout (see `NuGet.config`); once 1.1 is
published it resolves them from nuget.org.

## License

MIT — see [LICENSE](LICENSE).
