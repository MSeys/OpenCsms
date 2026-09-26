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
| In-process + Testcontainers | hosted by the suite (`AddAspNetCoreServer`) | started by the suite (Testcontainers) | verified: 41/41 green, twice, fresh containers, Chromium journeys included (R3.1) |
| Configured | hosted by the suite | an environment you provide through configuration; the suite's containers skip | verified twice, 41/41, one persistent database (R3.1) |
| Container topology (planned) | a container | containers, `docker compose` / Aspire | M4 |
| Published (planned) | a staging URL via `ProtoTest:Applications:Csms:BaseUrl` | the staging stack | M4 |

The suite's `Setup` is the same code in both modes, and infrastructure that the environment
already provides is not started. The product resolves its broker address on first publish and its
database string when the context is first resolved, so settings that arrive after registration are
seen; the framework gap that remains (the worker's `Program.Main`) is in the gap log below.

## Status

M1 and M2 are done, and M3's first stage (R3.1) is in: the operator dashboard's shell, the product's
sign-in and roles, and the two browser journeys. The OCPP 1.6J gateway, the charge-point simulator's
journey surface and the device-fit proof are in (R2.1/R2.2), including the idle-fee journeys that
advance the injected test clock instead of sleeping, and the error paths — a duplicate
StopTransaction, malformed MeterValues, an unknown charge point, out-of-subset actions and
out-of-range connectors — are pinned with the OCPP error each one answers with (R2.3). The remote
endpoints' failure branches are pinned too (R2.4): an offline charge point is `409`, a device that
refuses the call is `502`, no answer within the configured timeout is `504`, and a device's own
authorization decision (for example `Blocked`) is the `200` body's status, not a failure. The
gateway bills each session from the connector register it started at, so a second session on a
connector invoices its own energy only (R2.3). The suite is green
(`dotnet test tests/OpenCsms.Suite` → 41/41): container mode verified twice with fresh Testcontainers
including the two Chromium journeys (`artifacts/gates/opencsms-container-20260926-205015.log` and
`opencsms-container-20260926-205048.log`), and configured mode verified twice against one persistent
database (`opencsms-configured-20260926-205124.log` and `opencsms-configured-20260926-205144.log`) —
the Setup is the same code either way. The milestones below are the ones in the
[reference demo brief](https://github.com/MSeys/ProtoTest) — this README tracks them honestly.

[COVERAGE.md](COVERAGE.md) records what the suite asserts today and the areas that are still
untested.

- [x] M1 — CSMS API + PostgreSQL + billing worker + REST suite + one journey
- [x] M2 — OCPP gateway + charge-point simulator + idle-fee journey
- [ ] M3 — dashboard + monthly export + browser journeys (R3.1: shell, roles and browser wiring)
- [ ] M4 — container topology + deployed mode + fault injection + nightly CI

## OCPP 1.6J subset

The gateway speaks OCPP 1.6J over one WebSocket per charge point at `/ocpp/{chargePointId}`. The JSON
framing is `[2, id, action, payload]` for a call, `[3, id, payload]` for a call result and
`[4, id, code, description, details]` for a call error — the contract lives in
`src/OpenCsms.Domain/Ocpp`, shared by the gateway and the simulator. This is the whole subset:

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
happy path reads `Accepted`. `ChargePointsChargeOverOcpp` pins all four branches, and the suite
configures the timeout down to two seconds (`Setup`) so the journey that waits for a `504` stays fast.

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
sits on the same unauthenticated surface as `/api/tariffs` and `/api/stations`, and the suite's
`CsmsOperatorAttribute` uses it so every test's operator has a login.

**The read surface.** `/api/dashboard/{stations,stations/{id},stations/{id}/sessions,invoices,invoices/{id},tariffs}`
requires a signed-in session and scopes every query to the session's tenant claim — a tenant is
never taken from the request. `/api/status/stations` is the public page: station names, OCPP
identities, last-seen stamps and connector statuses, no tenant or pricing detail. The
operator commands are mirrored for the dashboard under `/api/dashboard` and require the `operator`
role: a viewer gets `403` before anything reaches a charge point, and the station is scoped to the
session's tenant first, so another tenant's station answers `404` exactly like an unknown one. The
machine API's `/api/stations/{id}/remote-start` and `/api/sessions/{id}/remote-stop` are unchanged.

**How the suite drives the browser.** The `Dashboard` application is the same API hosted on a
loopback listener inside the test process (`Support/LoopbackApplication.cs`, the documented
api-then-browser recipe) and publishes the address it bound as its `BaseUrl`; the readiness probe
waits on the real `/healthz`, and the browser session resolves that one address. The in-process
`AddAspNetCoreServer` application named `Csms` stays the REST and OCPP server, so provisioning and
the device journeys are untouched. The loopback instance receives the run's collected configuration
(database, broker, dashboard build), because a hand-built `WebApplication` has no initializer
forwarding it.

## Gap log

This is the honest state: M1 and M2 are done, and the reference-suite audit's fixes (R1a) landed before
new feature work.

- The worker's `Program.Main` cannot see the run's configuration before `Build()` (ProtoTest.Hosting
  P1-gap); the product reads its connection string inside the EF options factory. The fix is Phase 2
  audit A3.
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
- Published mode is planned (M4). The rest of OCPP beyond the named refusals, the reconnect storm,
  the dashboard's deeper views, payments and the `.xlsx` export have no tests yet —
  [COVERAGE.md](COVERAGE.md) lists the untested surface.
- **The dashboard's views are the R3.1 shell.** The session timeline detail, invoice view depth,
  tariff editing, the monthly `.xlsx` export and its Sheets assertions, a viewer-restriction journey
  and the multi-tenancy negative test are R3.2/R3.3; the roles and the tenant scoping they exercise
  are already in the API.
- **The management API is unauthenticated until M4.** `/api/tariffs`, `/api/stations`,
  `/api/sessions`, `/api/users` and the remote commands are the device/operator machine surface with
  no credentials yet; the dashboard's own reads and commands are cookie-authenticated. The cookie is
  `SameSite=Lax`; anti-forgery tokens land with the first dashboard mutation in R3.2.

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

`docker compose up` (M4) starts the whole topology for manual use.

## Packages

This repository consumes ProtoTest packages. While 1.1 is under development it resolves them from a
local feed produced by `eng/pack.ps1` in a sibling ProtoTest checkout (see `NuGet.config`); once 1.1 is
published it resolves them from nuget.org.

## License

MIT — see [LICENSE](LICENSE).
