# OpenCSMS

OpenCSMS is a small but real **EV charging management system** (CSMS) and the **reference suite for
[ProtoTest](https://github.com/MSeys/ProtoTest)**: a real product repository, not a sample inside the
framework repository.

It is deliberately a product, not a demo around a feature: a REST API, PostgreSQL, a billing worker on
RabbitMQ, a notification worker that pushes invoices and billing failures to external HTTP targets,
and the tests that prove a charging session becomes an invoice. ProtoTest is the only test
framework. Framework gaps this app exposes are recorded in the Gap log below.

## What this proves

One suite runs in every mode with the same Setup: each target declares its providers in order, and the first one the environment makes available serves it:

| Mode | API | PostgreSQL & RabbitMQ | State |
| --- | --- | --- | --- |
| In-process + Testcontainers | hosted by the suite (`UseInProcess`) | started by the suite (Testcontainers) | 75/75 green, 0 skipped, fresh containers, seven Chromium journeys and the showpiece journey included |
| Configured | hosted by the suite | an environment you provide through configuration; the suite's containers skip | 75/75, 0 skipped, against one persistent database migrated and backfilled from an earlier schema |
| Published, locally rehearsed (`-Mode published`) | a real API process at `ProtoTest:Applications:Csms:BaseUrl`; the real `OpenCsms.Billing.Worker` consumes and the real `OpenCsms.Notification.Worker` idles without targets | running `opencsms-postgres` / `opencsms-rabbitmq` containers addressed by the configured keys | 61 passed, 13 skipped of 74: the seven clock journeys, the two outbox substitutions and the four notification journeys skip, and the showpiece journey does not run there and is not reported skipped; no staging target exists yet |
| Container topology (`-Mode topology`) | the AppHost's API project resource at its published address (it also serves the dashboard) | the AppHost's PostgreSQL and RabbitMQ containers | 61 passed, 13 skipped of 74: the seven clock journeys, the two outbox substitutions and the four notification journeys skip, and the showpiece journey does not run there and is not reported skipped |

The suite's `Setup` is the same code in every mode: each target - the store, the broker, the API
with its billing worker, the dashboard application - declares its providers in order, and the first
one the environment makes available serves it. Infrastructure the environment already provides is
not started, and a worker follows its application: hosted with the in-process server, run by the
environment otherwise. The product resolves its broker address on first publish and its database
string when the context is first resolved, so settings that arrive after registration are seen.

The topology leg is `src/OpenCsms.AppHost`: an Aspire AppHost that declares PostgreSQL, RabbitMQ,
the API project (which also serves the dashboard) and both workers as project resources, and injects
the store and broker under the product's own keys. It targets net8.0 - the suite's target framework -
because the Aspire testing host runs the AppHost's entry point inside the test process, and DCP
launches the project resources with `dotnet run`, which cannot choose a target framework; every
OpenCSMS project is net8.0. `eng/run-suite.ps1 -Mode topology` sets the integration's one selection
key, `ProtoTest__Aspire__Enabled=true`, so the same `Setup` chains resolve through the AppHost and
the environment runs the workers. The AppHost serves only while selected: a run that sets no
selection key never starts it - the container leg's own Testcontainers are the only infrastructure -
and a run that configures every key the AppHost fills steps it aside entirely. A run that exports
the store or broker keys keeps them: the graph declares a resource only for a key the run leaves
unset and injects the provided value instead.

## What is built

The API serves tenant, tariff, station and session management behind per-tenant API keys, plus the
dashboard's read and command surface behind a signed-in cookie. The OCPP 1.6J gateway serves charge
points over one WebSocket each, with a charge-point simulator as the suite's device. The billing
worker consumes `session.ended`, stores exactly one invoice per session through an outbox, and
publishes `invoice.issued`; the notification worker pushes invoice-ready and billing-failure
notifications to configured HTTP targets and dead-letters a target that stays down. The operator
dashboard is a Vue SPA served by the API: sign-in, roles, the session timeline, invoice calculation
lines, tariff repricing and the monthly `.xlsx` export.

The suite is green in the default and configured modes (75/75 on fresh Testcontainers, 75/75 against
the persistent database, 56/56 domain rules). [COVERAGE.md](COVERAGE.md) records what the suite
asserts today and the areas that are still untested.

**Layering.** The solution is concentric layers, and a project depends only inward:
`Domain` ← `Application` ← `Infrastructure` / `Protocol.Ocpp` / `Api` / `Billing.Worker` /
`Notification.Worker`, with `Contracts` on the side and `AppHost` outside the product layers.

| Project | Owns |
| --- | --- |
| `src/OpenCsms.Domain` | Aggregates, value objects and invariants; no EF, HTTP or JSON. |
| `src/OpenCsms.Application` | The use cases, over narrow persistence, event and clock ports; no infrastructure. |
| `src/OpenCsms.Contracts` | The integration events and the API's request/response DTOs; no project references. |
| `src/OpenCsms.Infrastructure` | The EF `DbContext` with its fluent configurations, migrations and stores, and the RabbitMQ publisher and topology. |
| `src/OpenCsms.Protocol.Ocpp` | The OCPP 1.6J wire format, translated to application commands. |
| `src/OpenCsms.Api`, `src/OpenCsms.Billing.Worker`, `src/OpenCsms.Notification.Worker` | Thin composition roots: they bind requests and deliveries and map answers, status codes and DTOs. |
| `src/OpenCsms.AppHost` | The Aspire topology: the containers and project resources the topology mode starts, with the store and broker injected under the product's keys; no product code. |

## OCPP 1.6J subset

The gateway speaks OCPP 1.6J over one WebSocket per charge point at `/ocpp/{chargePointId}`. The JSON
framing is `[2, id, action, payload]` for a call, `[3, id, payload]` for a call result and
`[4, id, code, description, details]` for a call error: the framing contract lives in
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
charge point no station is registered for; its `BootNotification` is answered `Rejected`. The suite's
charge point is `AcCharger`, built on `ProtoTest.Devices.WebSocket`: the same registration reaches the
in-process gateway through its `TestServer` and a real endpoint over a socket, with no mode
conditionals.

The two remote endpoints answer a failed call honestly: a charge point that is not connected, or
whose connection is lost while the call is in flight (for example a reconnect replacing it), is
`409 Conflict` (both endpoints); a device that refuses the call with an OCPP call error is
`502 Bad Gateway`; and one that takes the call but does not answer within `Ocpp:RemoteCallTimeoutSeconds`
(ten seconds by default) is `504 Gateway Timeout`. A device's own authorization decision, for example
`Blocked`, is not a failure: the operator reads it from the `200` body's `status`, exactly as the
happy path reads `Accepted`. `ChargePointsChargeOverOcpp` pins the four branches, and `OcppErrorPaths`
pins the reconnect race: the call whose connection dies is refused `409`, and the operator's next
call is answered by the reconnected charge point through the live connection. The journey that
waits for a `504` reads the API's effective timeout (two seconds while the suite hosts the test
server, the environment's own `Ocpp:RemoteCallTimeoutSeconds` against a running stack) instead of
assuming either.

## Operator dashboard

The operator dashboard is a Vue 3.5 + vue-router + Vite SPA in `src/OpenCsms.Dashboard`, served by
the API at the site root from `Csms:Ui:Path` (default `../OpenCsms.Dashboard/dist`, relative to the
API's content root) with a static-files pass and an SPA fallback: `/api`, `/ocpp`, `/healthz` and
`/swagger` keep their namespaces, so an unmatched path under them answers 404 instead of the SPA page. Without a
build the dashboard routes answer a "not built" page and everything else keeps working. The
dashboard folder carries its own [development notes](src/OpenCsms.Dashboard/README.md).
`eng/build-dashboard.ps1` runs `npm ci` + `npm run build` (vue-tsc + vite); `eng/run-suite.ps1`
builds it before every suite run, so the browser journeys always test a fresh bundle.

Routes the SPA ships: `/sign-in`, `/` (stations), `/stations/:stationId` (sessions),
`/invoices`, `/invoices/:invoiceId`, `/tariffs`, `/status` (public). The suite's web session
declares `DiscoverRoutes`, so page coverage comes from the live Vue Router.

**Sign-in and roles.** `POST /api/auth/sign-in` exchanges a tenant-scoped account's email and
password (PBKDF2-SHA256 hashes in the `Users` table) for an HttpOnly cookie; `GET /api/auth/session`
answers the SPA, `POST /api/auth/sign-out` clears it. A user is `operator` (admin) or `viewer`.
`POST /api/users` provisions an account and sits on the machine surface: it carries the tenant's API
key and creates the account for the credential's tenant. The suite registers each test's tenant
through `POST /api/tenants` (the answer carries the key once) and then provisions the tariff,
station, operator and viewer through the keyed routes: the routes stay the front door, with the
mechanics on `ProtoTest.Data` provisioners (`tests/OpenCsms.Suite/Support/CsmsProvisioners.cs`,
orchestrated by `CsmsProvisioning` for `CsmsOperatorAttribute`, the neighbor tenants, the viewers and
the contract tests). Provisioned rows stay, because the API has no delete route; unique names keep
reruns against a database that outlives the test process independent.

**The read surface.** `/api/dashboard/{stations,stations/{id},stations/{id}/sessions,invoices,invoices/{id},tariffs}`
requires a signed-in session and scopes every query to the session's tenant claim: a tenant is
never taken from the request. `/api/status/stations` is the public page: station names, OCPP
identities, last-seen stamps and connector statuses, no tenant or pricing detail. The
operator commands are mirrored for the dashboard under `/api/dashboard` and require the `operator`
role: a viewer gets `403` before anything reaches a charge point, and the station is scoped to the
session's tenant first, so another tenant's station answers `404` exactly like an unknown one
(`TenantIsolation` pins both directions). The machine API's
`/api/stations/{id}/remote-start` and `/api/sessions/{id}/remote-stop` require the tenant's machine
key and scope the same way, so neither surface can reach the other tenant's rows
(`MachineApiCredentials` pins the machine side).

**The views.** The station screen is the session timeline (connector, start/end,
energy, state and the invoice link for billed sessions, matched client-side from the invoice
list), and the operator admin gets the remote-start panel on it; viewers never see the panel.
The invoice screen shows the worker's calculation lines (energy, start fee, idle fee, total).
Tariff repricing is the operator admin's form on the tariffs screen
(`PUT /api/dashboard/tariffs/{id}`, `operator` role, tenant-scoped `404`, negative prices `400`);
viewers read the table with a read-only note instead. Stored invoices keep the prices they were
billed at: only new sessions bill at the new ones (`TariffEditingContracts` bills one session
each side of a repricing to prove it). Cookie mutations carry the anti-forgery token the SPA
fetches from `GET /api/auth/xsrf` as `X-XSRF-TOKEN`; without one the API answers `400`.

| Screen | operator admin | viewer |
| --- | --- | --- |
| Stations, station timeline, invoices, invoice lines, tariffs, public status | reads | reads |
| Remote start/stop, tariff repricing, monthly download | acts (token + role) | hidden in the UI, `403` at the API |

**The monthly export.** `GET /api/invoices/export?month=YYYY-MM` answers the tenant's
month as a real `.xlsx` download (`invoices-YYYY-MM.xlsx`): an `Invoices` sheet with one row per
stored invoice issued inside the UTC month (oldest first) and a `Summary` sheet with the month,
the row count and the summed total. The composition lives in the `MonthlyInvoiceExport`
application use case over the invoice queries; the spreadsheet mechanics live in Infrastructure
(`ClosedXmlInvoiceExportWriter`, behind the `IInvoiceExportWriter` port), because the workbook
library is an infrastructure detail the application never sees. ClosedXML was chosen for its
small surface and its MIT license. EPPlus's noncommercial license does not fit this MIT
repository, and raw OpenXML is needlessly verbose for a two-sheet table. Totals are written as
literal values, never formulas, so the file carries exactly what the invoice rows hold. A missing
or malformed month is `400`, anonymous reads are `401`, and the numbers always match the invoice
rows; the journey sums both sides.

**The seeded busy month.** The suite seeds volume once per run
(`Support/SeededMonth.cs`, an `AddRunSetup` step after the containers): two tenants sharing May
2030, each with a tariff, a station, an operator admin and 120 billed sessions (5 to 25 kWh, no idle
fee), composed from the product's own application services in-process against the run's database.
The worker and the broker are not involved: issuance runs with its events dropped. The seeded
tariff's name is the idempotency marker: a rerun against a database that already holds it stores
nothing, so configured-mode reruns share one seed. `ProtoTest__Seed=off` makes the run store
no seed and skips the six seeded journeys (see COVERAGE.md). Per-test prerequisites still go through the
REST front door: the same routes, with the mechanics on `ProtoTest.Data` provisioners; the seed
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

## Notifications

The notification worker (`src/OpenCsms.Notification.Worker`) consumes the product's events and pushes
them to external HTTP targets:

| Notification | Trigger | Target |
| --- | --- | --- |
| invoice-ready | `invoice.issued`, published by the billing worker after it stores an invoice | `Notifications:InvoiceReadyBaseUrl`: the PSP or email relay, `POST {base}/notifications/invoice-ready/{invoiceId}` |
| billing failure | `billing.failed`, published by the billing worker when a `session.ended` exhausted its retries and was dead-lettered | `Notifications:BillingFailureBaseUrl`: the operator's alerting webhook, `POST {base}/notifications/billing-failed/{sessionId}` |

Each payload carries the event's own facts (the invoice's ids, total, currency and instant, or the
session, the reason and the instant), and the entity id in the path, so a target can name what
arrived. A delivery the target rejects (a non-2xx answer) or that throws is retried in-process three
times (the consumer loop's republished retry) and then dead-lettered to `notifications.dlx`
(`notifications.invoice-issued.dlq` or `notifications.billing-failed.dlq`), so a target that stays
down loses nothing silently. A kind whose base address is not configured stays idle and logs why.

The suite points both addresses at `ProtoTest.WireMock` fakes. They are per-run and listen on ports
reserved at setup, because the worker reads its addresses once when its host starts; a journey names
its invoice or session in the request path and reads that request and its logged body out of the
shared log. `InvoiceNotificationsReachTheExternalTarget` proves an issued invoice reaches the
invoice-ready fake with the invoice's data, and that a session the billing worker gives up on reaches
the failure fake with the recorded reason. `NotificationTargetOutagesAreDeadLettered` pins the
target-down path: the fake answers `503` for one entity's notifications, the consumer spends its
in-process retries and the delivery lands on that notification's dead-letter queue
(`notifications.invoice-issued.dlq` or `notifications.billing-failed.dlq`) with the entity's ids and
the completed-retry count, while the invoice stays stored exactly once. A target-down rejection is
keyed to the notification's own session, so it runs beside the accepting journeys on the shared
fakes.

## Benchmarks and the trace showpiece

The benchmark harness (`tests/OpenCsms.Benchmarks`) runs the product's own API and billing worker and
measures ProtoTest's per-test cost against the same API behind a raw `WebApplicationFactory`, with
PostgreSQL and RabbitMQ from the environment. Rerun it with:

```bash
pwsh eng/run-benchmark.ps1
```

It writes `artifacts/benchmarks/<timestamp>/results.json` and `results.md`. A run of the harness
(Windows 11 / X64 / 16 cores / 31 GiB, .NET 8.0.31, 1,000 iterations and 1,000 seeded journeys)
measured:

| Comparison | ProtoTest | Raw WebApplicationFactory | Where ProtoTest loses |
| --- | ---: | ---: | --- |
| One `GET /healthz` through a full test cycle, tracing on | 35.2 - 36.2 ms median | 4.8 - 5.0 ms median | ~7x per test |
| The same test, tracing off | 30.7 - 32.4 ms median | 4.8 - 5.0 ms median | ~6x per test |
| Suite startup through the first completed request | 156 - 179 ms | 73 - 83 ms | ~2.1x |

Tracing itself is not the cost: with it off the same test measured about the same (about 31-32 ms vs.
35-36 ms, within run variation). Most of the per-test difference is the suite's own broker taps, which
prepare and release inside every test (the run's phase profile measures about 30 of 36 ms there);
ProtoTest's lifecycle and context wrapper is a few milliseconds. The raw baseline builds one factory
and a client per test, so it pays for the client and the request, and it never opens a broker
consumer. The raw baseline is the honest floor: an integration test that wants a fresh host per test
pays more than either number. The 1,000-journey run left a 38.8 MB trace with a 49.3 to 54.8 ms
journey median; the same run at 100 journeys left 4.1 MB, so the trace grows linearly with the test
count, about 39 KB per journey.

`IdleFeeAfterTariffChange` pins the tariff-snapshot rule: a session that starts under one tariff
and ends after a reprice bills the tariff it started under (20.30, not 13.60). The pre-fix failing
trace is kept at `artifacts/showpiece/opencsms.prototrace`. The session copies the tariff's terms
when it starts, so the repricing never reaches it: the journey asserts 20.30 and runs in the
container and configured modes. The published and topology legs do not report it (see the mode
table above). `pwsh eng/run-showpiece.ps1` reruns it alone and writes the
fresh trace to `artifacts/showpiece/opencsms-rerun.prototrace`, leaving the historical one untouched.

## Gap log

The honest state of the product and the suite, including what is not built yet.

- **Ending a session is transactional (outbox).** `/api/sessions/{id}/end` stores the ended
  session and its `session.ended` event in one transaction; the immediate publish attempt may fail
  without losing the invoice - the dispatcher retries the stored row with a bounded backoff until the
  broker accepts it, and the worker's idempotency keeps exactly one invoice per session. The
  publish-failure paths are pinned by `OutboxTests`: one failed attempt, and a two-failure outage the
  store's bounded backoff rides out, both still billing exactly once.
- **Every test connects to the broker at setup.** The run-wide
  `Tap(CsmsEvents.Exchange, CsmsEvents.DeadLetterExchange, CsmsEvents.NotificationsDeadLetterExchange)`
  pre-bind makes the suite's tap prepare its destinations during test setup, including the REST-only
  contract tests, so a run without a broker fails setup there rather than skipping; the broker
  capability is what gates the tests that need it, and the pre-bind is suite-wide by design.
  Recorded in [COVERAGE.md](COVERAGE.md).
- **The DLQ assertions await the dead-letter exchanges.** ProtoTest's messaging surface publishes and
  awaits by `(exchange, routingKey)` (`PublishAsync(exchange, routingKey, payload)`,
  `AwaitAsync(exchange, routingKey, predicate, …)`) and carries the routing key on consumed messages;
  a tap binds a test-owned queue to the awaited exchange. The product's dead-letter queues hang off the
  fanout exchanges it declares (`csms.events.dlx`, `notifications.dlx`), so the journeys publish the
  poison with the product's routing key and await the dead-letter exchange, whose own binding carries
  the same delivery into the queue.
- **A browser download is named binary content.** `WebDownload` implements `IProtoBinaryContent`,
  so the invoices screen's download journey opens the captured export in one line.
- **Container topology is verified.** `eng/run-suite.ps1 -Mode topology` selects `OpenCsms.AppHost`
  (it starts only while selected), which runs the API project (serving the dashboard too), the
  billing worker and the notification worker as real processes beside fresh PostgreSQL and RabbitMQ
  containers: 61 passed, 13 skipped of 74 (the seven clock journeys, the two outbox substitutions
  and the four notification journeys); the showpiece journey does not run there and is not reported
  skipped. The AppHost's notification
  worker is wired to the product's own target keys, which the suite leaves unset there, so it idles
  with the product's log line; the notification journeys themselves assert the injected run clock and an
  in-process application, which real processes do not have, so they stay skipped rather than being
  pointed at the suite's lazily-started WireMock fakes. Payments are not built: the notification
  worker pushes invoice-ready and billing-failure notifications to configurable HTTP targets (the
  suite's WireMock fakes), but there is no real PSP payment flow, and a target that stays down is
  dead-lettered rather than replayed (`NotificationTargetOutagesAreDeadLettered` pins it). Published
  mode is a self-contained local rehearsal (`-Mode published`, the mode table above) and has no
  staging target yet; the rest of OCPP beyond the named refusals has no tests yet;
  [COVERAGE.md](COVERAGE.md) lists the untested surface.
- **The Aspire and WireMock packages disagree about Humanizer.** The Aspire testing host resolves
  `Humanizer.Core` 3.0.10 while WireMock's Handlebars helpers still ask for the 2.14.1 satellite set,
  so a suite composing both fails restore with `NU1608` as an error (the suite builds with
  `TreatWarningsAsErrors`); the suite pins `Humanizer` 3.0.10 to move every satellite to the version
  the Aspire graph already resolved.
- **The dashboard's views.** The timeline, invoice lines, tariff repricing,
  the export with its Sheets assertions, the viewer journey and the multi-tenancy negative test
  are in; the invoices screen's download button is clicked in Chromium with the downloaded bytes
  asserted as real cells, and the remote-stop button drives a connected charge point from the
  station screen.
- **The export speaks the invoice API's names.** The `Invoices` sheet headers are `IssuedAtUtc`,
  `StartFeeAmount` and `IdleFeeAmount`, exactly as `InvoiceResponse` names them; the per-row
  journey fails on a swapped or renamed column.
- **The machine management API requires a per-tenant API key.** `POST /api/tenants` is the one
  anonymous management route: it registers a tenant and answers with the tenant id and its API key
  once; only the key's SHA-256 hash is stored. Every other management route (`/api/tariffs`,
  `/api/stations`, `/api/sessions`, `/api/users`, the invoice reads and the machine remote commands)
  requires `X-Api-Key` and acts on the credential's tenant: never on a tenant named in the request.
  A missing or unknown key answers `401`; another tenant's row answers `404` exactly like an unknown
  one, and a dashboard cookie does not unlock the surface. The suite registers each test's tenant
  through that route and carries the key on every management call; `MachineApiCredentials` pins the
  `401`s without a key and with a wrong key, that a refused call stored nothing, and that each key
  reaches only its own tenant's rows. The dashboard's reads, commands and export stay
  cookie-authenticated, and the OCPP gateway and public status routes are unchanged.

## Prerequisites

The .NET 10 SDK with the .NET 8 runtime (the 8.0.x SDK installs both; CI installs both SDKs),
Node 22 or later, and a container runtime. The browser journeys need Chromium with its
system libraries; build the suite once, then install it the way CI does:

```bash
pwsh tests/OpenCsms.Suite/bin/Release/net8.0/playwright.ps1 install --with-deps chromium
```

## Running

```bash
pwsh eng/run-suite.ps1 -Mode container
```

On a machine with a container runtime this builds the dashboard, then runs the whole suite. The
suite starts PostgreSQL and RabbitMQ itself. Without a runtime, provide the environment through
configuration; all three declared keys must be set for the suite's containers to skip:

```powershell
$env:ConnectionStrings__Csms = "Host=localhost;Database=opencsms;Username=opencsms;Password=opencsms"
$env:Messaging__RabbitMq__ConnectionString = "amqp://guest:guest@localhost:5672"
$env:ProtoTest__Messaging__RabbitMq__ConnectionString = "amqp://guest:guest@localhost:5672"
pwsh eng/run-suite.ps1 -Mode configured
```

(On bash the same three keys are `export`ed with the same names and values.)

The first key is the product's database, the second the product's broker, and the third the address
ProtoTest's own messaging tap uses.

Each mode runs through `eng/run-suite.ps1`: `pwsh eng/run-suite.ps1 -Mode container`,
`pwsh eng/run-suite.ps1 -Mode configured` (the latter requires the three keys above),
`pwsh eng/run-suite.ps1 -Mode published` (which starts the rehearsal stack itself, below) and
`pwsh eng/run-suite.ps1 -Mode topology` (which clears the published-mode keys and sets
`ProtoTest__Aspire__Enabled=true`, so the AppHost starts and its resources serve the store, the
broker, the API and the dashboard) stream the run to the console and tee it to
`artifacts/gates/opencsms-<mode>-<timestamp>.log`. All modes build the dashboard first
(`eng/build-dashboard.ps1`: `npm ci` + `npm run build`), so the browser journeys always test a fresh
bundle; run it alone to build without running the suite.

`pwsh eng/run-suite.ps1 -Mode published` is the local rehearsal and is self-contained beyond the
persistent containers: against the running `opencsms-postgres` / `opencsms-rabbitmq` stack it builds
the product, starts the API and both workers as real processes (`dotnet
src/OpenCsms.Api/bin/Release/net8.0/OpenCsms.Api.dll --urls http://127.0.0.1:5080 --contentRoot
<repository>/src/OpenCsms.Api`, `OpenCsms.Billing.Worker.dll` and
`OpenCsms.Notification.Worker.dll`), waits for `/healthz`, exports the five keys
(`ConnectionStrings__Csms`, `Messaging__RabbitMq__ConnectionString`,
`ProtoTest__Messaging__RabbitMq__ConnectionString`, `ProtoTest__Applications__Csms__BaseUrl` and
`ProtoTest__Applications__Dashboard__BaseUrl`), runs `dotnet test tests/OpenCsms.Suite -c Release
--no-build`, and stops the processes again. It clears mode keys a shell may carry first, so reruns
measure the same stack. The notification worker stays idle without target keys (its `is idle` line
is captured beside the suite log) because the suite's fakes live inside the test process; nothing
leaves the machine. Start the persistent stack once with:

```bash
docker run -d --name opencsms-postgres -e POSTGRES_USER=opencsms -e POSTGRES_PASSWORD=opencsms -e POSTGRES_DB=opencsms -p 5432:5432 postgres:16-alpine
docker run -d --name opencsms-rabbitmq -p 5672:5672 rabbitmq:3-alpine
```

To point the same suite at a stack someone else runs, set those five keys in the environment, build the dashboard
with `pwsh eng/build-dashboard.ps1`, and run `dotnet test` yourself: the suite's provider chains
step aside, the environment runs the workers and its clock,
and the clock-dependent journeys skip. The script writes the suite log under
`artifacts/gates/opencsms-published-<timestamp>.log`, with each process's output in
`opencsms-published-<timestamp>-<name>.log` beside it.

A compose file for the deployment-shaped rehearsal is not built yet; the published leg runs the
product's own processes against the two containers, and the topology leg starts the same resources
through the AppHost instead.

## CI

Three GitHub Actions workflows map to the mode table; each one runs the same command a developer
runs locally, so CI proves the modes rather than a CI-shaped path around them:

| Workflow | Trigger | Runs |
| --- | --- | --- |
| `.github/workflows/ci.yml` | push to `main`, pull request, dispatch | `eng/run-suite.ps1 -Mode container`: the fast leg, the in-process product on fresh Testcontainers, the Chromium journeys included |
| `.github/workflows/nightly.yml` | nightly schedule, dispatch | `eng/run-suite.ps1 -Mode topology`: the AppHost leg, its containers and the product's worker processes |
| `.github/workflows/staging-smoke.yml` | dispatch only | published mode against a stack someone else runs: the five keys (`ConnectionStrings__Csms`, `Messaging__RabbitMq__ConnectionString`, `ProtoTest__Messaging__RabbitMq__ConnectionString`, `ProtoTest__Applications__Csms__BaseUrl`, `ProtoTest__Applications__Dashboard__BaseUrl`) from repository secrets, then `dotnet test` |

Each job builds the dashboard, installs Chromium with its system libraries, and uploads the mode's
run log from `artifacts/gates/`.

This repository has no remote yet, so no workflow has run on GitHub; the evidence is the local
rehearsal of each leg. Delete this note when the first hosted run lands. A hosted run restores
from the committed `NuGet.config`, so it needs ProtoTest 1.1.0 on nuget.org (see [Packages](#packages)).
The staging smoke has no target yet: a
dispatch without all five secrets fails with the missing key names before anything starts.

## Packages

This repository consumes ProtoTest packages from nuget.org (see `NuGet.config`); the pins are
already `1.1.0`. [COVERAGE.md](COVERAGE.md#prototest-integration-matrix)
records which of them the suite exercises and the reason recorded for each one the product's shape
does not justify.

## License

MIT: see [LICENSE](LICENSE).
