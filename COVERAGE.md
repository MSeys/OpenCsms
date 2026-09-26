# Coverage — what the suite asserts today

The honest scope of the suite, kept in step with the tests. Linked from the README; update it in
the same commit as the behavior it describes.

## Tested

| Where | What it asserts |
| --- | --- |
| `tests/OpenCsms.Suite/Journeys/ChargingSessionsBecomeInvoices.cs` | The M1 journey: the provisioned operator starts a session over REST, records 22 kWh and ends it; the billing worker consumes `session.ended` and publishes `invoice.issued`, which the suite awaits on the product exchange; the stored invoice is read back over REST, and the amounts are checked (energy 8.80 + start fee 1.50 = 10.30 EUR, no idle time). Both the event and the stored row must carry the run clock's fixed instant, so the injected `TimeProvider` is observable (R1a-04). |
| `tests/OpenCsms.Suite/Journeys/ChargePointsChargeOverOcpp.cs` | The M2 gateway journeys (R2.1/R2.2/R2.4), through `ProtoTest.Devices.WebSocket` and the in-process `TestServer` transport. Journey one: a charge point boots, heartbeats, reports connector status, runs a 22 kWh session from StartTransaction to StopTransaction with MeterValues, and the API view (station last-seen, connector status, session transaction/energy/end) matches; StopTransaction publishes `session.ended`, so the worker's `invoice.issued` arrives for a session no REST call created. Journey two: the operator's `remote-start`/`remote-stop` reach the connected charge point through the simulator's `AnswerRemoteStartAsync`/`AnswerRemoteStopAsync`, the REST request waits for the device's own answer, and the device's StopTransaction ends the session. Four journeys pin the remote failure branches (R2.4): an offline charge point answers both endpoints `409` naming it, opening or ending nothing; a device that refuses the call with an OCPP call error answers `502` and a refused stop leaves its session open; a device's own `Blocked` decision is answered `200` with that status and starts nothing; and a device that takes the call and never answers makes the endpoint answer `504` with the configured two seconds in its message (the suite configures `Ocpp:RemoteCallTimeoutSeconds`, default 10, down to 2 in Setup) and leaves the addressed session open. The idle-fee journeys bend the clock through the simulator's `PlugInAsync`/`MeterValuesAsync`/`UnplugAsync`: a car that stays plugged in five hours (grace: ten minutes) is billed five started idle hours — 8.80 energy + 1.50 start fee + 10.00 idle = 20.30 — and a car that unplugs five minutes in is billed energy and the start fee only (10.30). The register journey (R2.3) runs two sessions on one connector whose register keeps counting across them: the first ends at 22 kWh, the second starts at that register and adds 5 kWh, and each invoice bills only its own energy (10.30 and 3.50), never the 27 kWh register. The idle fee is the end-to-end proof that the gateway stamps from the test's advanced clock, not the run's. |
| `tests/OpenCsms.Suite/Journeys/OcppErrorPaths.cs` | The M2 error paths (R2.3), read off the call errors the gateway answers them with. A duplicate StopTransaction is answered `Accepted` without ending the session again — its `EndedAtUtc` stays at the first stop's instant even after the clock moved a minute — and the store still holds one invoice row with the amounts the first stop produced. Malformed MeterValues get the code their rule names and leave the session open at its previous energy: a non-numeric sample and a sample without the energy measurand are `FormationViolation`, a reading below the session's last one is `PropertyConstraintViolation`. An unknown charge point boots `Rejected` and every other call is refused `InternalError` naming the missing station (the identity check precedes the payload read). Each of the eight named out-of-subset actions (Authorize, DataTransfer, Reset, UnlockConnector, UpdateFirmware, FirmwareStatusNotification, DiagnosticsStatusNotification, SetChargingProfile) is refused `NotImplemented` and touches no station, connector or session state. StatusNotification and StartTransaction for a connector outside the station's two are refused `PropertyConstraintViolation` with no connector or session recorded. A second StartTransaction on a charging connector is answered `ConcurrentTx` with the running transaction's number: one session stays open at zero energy. A charge point that drops and reconnects five times is answered `Accepted` on every boot, its session stays open and untouched, and a remote start forwarded afterwards is answered by the live connection, proving the registry's single entry. |
| `tests/OpenCsms.Domain.Tests/OcppProtocolTests.cs` | The OCPP 1.6J framing contract: call/result/error shapes serialize exactly, typed payloads parse with OCPP's wire names and string enums, malformed frames and members outside the contract are refused, and the energy register reads Wh and kWh while rejecting unreadable, negative or non-energy samples. |
| `tests/OpenCsms.Domain.Tests/StationTests.cs` | The domain rules the gateway relies on: a station needs a charge point identity, `MarkSeen` stamps the last-seen instant, and a connector's reported status replaces the earlier one. |
| `tests/OpenCsms.Domain.Tests/UserTests.cs` | The account rules behind the dashboard's sign-in (R3.1): email normalization and the role are kept at creation, short passwords are refused, a salted hash verifies only the exact password, two accounts with the same password carry different hashes, and `UserRoles` round-trips the wire spellings. |
| `tests/OpenCsms.Suite/Api/SessionContracts.cs` | Synchronous REST contracts: a connector outside the station answers 400; an unknown session has no invoice (404). No worker or broker needed. |
| `tests/OpenCsms.Suite/Api/DashboardContracts.cs` | The dashboard's API contracts (R3.1, R3.2): anonymous reads of `/api/dashboard/stations` answer 401 while `/api/status/stations` answers 200 without an account; a signed-in operator reads their own station and its empty session list; a viewer reads the same list (200) but `/api/dashboard/stations/{id}/remote-start` answers 403 before anything reaches a charge point; the operator's own call reaches the product path and answers 409 because the provisioned charge point is not connected. The cookie is what carries the role; the suite signs in through the product's own endpoint, exactly as the SPA does. Since R3.2 the dashboard mutations carry the anti-forgery token from `GET /api/auth/xsrf`, exactly as the SPA sends it. |
| `tests/OpenCsms.Suite/Api/TariffEditingContracts.cs` | The tariff repricing contracts (R3.2): anonymous repricing is 401, a viewer with a valid token is 403, an operator without the token is 400, another tenant's tariff is 404, and a negative value in any of the four prices - energy, start fee, idle fee, grace period - is 400 with all four prices provably unchanged. The billing proof bills one session each side of a repricing: the stored invoice keeps 8.80/10.30 while the new session bills 12.10/13.60 at the new energy price. |
| `tests/OpenCsms.Suite/Api/TenantIsolation.cs` | The multi-tenancy negative test (R3.2): one operator's reads of another tenant's station, station sessions and invoice answer 404 exactly like unknown ids, in both directions, and the station/tariff lists carry only the signed-in tenant's rows. The foreign invoice comes from the run's seeded month and the foreign session is an open REST session, so no broker or worker is needed; the second test pins the seeded tenants against each other (120 invoices each, no shared id). |
| `tests/OpenCsms.Suite/Journeys/MonthlyInvoiceExport.cs` | The monthly export journey (R3.2), asserted with `ProtoTest.Sheets` on the real cells: the `Invoices` sheet carries the header plus one row per seeded invoice, the header uses the invoice API's own names (`IssuedAtUtc`, `StartFeeAmount`, `IdleFeeAmount`), the typed model matches every rule, every row falls in 2030-05, every exported row carries its stored row's own numbers (a swapped column fails the per-row check, not just the sum), and the summed total equals the dashboard invoice rows' sum for the same tenant and month, with identical row identity. A month with no invoices still carries the header with zero totals; malformed and missing months are 400; anonymous reads are 401; and the two tenants' exports share no invoice id. |
| `tests/OpenCsms.Domain.Tests/TariffPricingTests.cs` | The repricing domain rules (R3.2): all four prices are replaced together, identity/tenant/name/currency never change, and a refused update changes nothing. |
| `tests/OpenCsms.Suite/Web/OperatorDashboardJourney.cs` | The R3.1 browser journey: Chromium signs in on the dashboard's own `/sign-in` screen with the account `CsmsOperatorAttribute` provisioned, the stations list shows the one station this test's tenant owns (name, OCPP identity, connector count) and its detail screen shows the empty session list. The session declares `DiscoverRoutes`, so the report's page inventory comes from the live Vue Router (`/sign-in`, `/`, `/stations/{id}`, `/invoices`, `/invoices/{id}`, `/tariffs`, `/status`). |
| `tests/OpenCsms.Suite/Web/DashboardDepthJourney.cs` | The R3.2 depth journey: a real billed session (22 kWh, 10.30 EUR) drives the station timeline — connector, energy, ended state and the invoice link — and the invoice screen shows the worker's calculation lines (8.80/1.50/0.00/10.30). The operator reprices the tariff through the dashboard form (the table shows €0.55 afterwards) and the remote start against the unconnected charge point refuses honestly on the screen. |
| `tests/OpenCsms.Suite/Web/InvoiceExportJourney.cs` | The invoices screen's download journey (R3.4): Chromium fills the export month, clicks the screen's own download button, and the captured file is asserted with `ProtoTest.Sheets` on the real cells - the suggested file name (`invoices-2030-06.xlsx`), the API-named header, the one billed row's energy and total, and the summary's month, count and total. |
| `tests/OpenCsms.Suite/Web/RemoteStopJourney.cs` | The dashboard remote-stop journey (R3.4): the charge point behind the loopback listener is connected through the dashboard application's own device client, so the operator's stop button on the open session reaches it; the device answers with the running transaction, the screen reports the answer, and the device's own stop transaction ends the session at 5 kWh - the UI path of the API journey's remote stop. |
| `tests/OpenCsms.Suite/Web/ViewerRestrictionJourney.cs` | The R3.2 viewer journey: the viewer provisioned by `CsmsViewerAttribute` signs in through the sign-in screen, reads stations, the station detail, tariffs and invoices — and the operator actions are not rendered (no remote-start panel, no per-session stop button on the open session row, no tariff edit buttons, the read-only note instead). The API behind them already answers 403. |
| `tests/OpenCsms.Suite/Web/TenantIsolationJourney.cs` | The R3.2 browser isolation journey: the stations list shows only the signed-in tenant's station (the neighbor's row is absent) and opening the neighbor's station answers the not-found panel. |
| `tests/OpenCsms.Suite/Web/PublicStatusJourney.cs` | The R3.1 public journey: with no sign-in and no cookie, the browser opens `/status` and finds the provisioned station's card (name, OCPP identity, the honest "no connector status reported yet" state); the shell offers sign-in and shows no session facts, which is what "public" means here. |
| `tests/OpenCsms.Suite/Billing/DeadLetterTests.cs` | The worker's negative path, through the raw broker: a `session.ended` for an unknown session (published on the product exchange with the product routing key) is retried three times and dead-lettered; the test consumes `billing.session-ended.dlq` within a bounded wait and asserts the payload session id and the `x-opencsms-retries: 3` header (completed retries; the fourth attempt was dead-lettered). Needs worker and broker, no REST. |
| `tests/OpenCsms.Suite/Billing/RedeliveryTests.cs` | The worker's republish-on-redelivery path (R1a-01): after the journey's setup created the invoice, the product's own `session.ended` payload is published a second time on the product exchange and routing key; `invoice.issued` must arrive again with the same invoice id, and the store must still hold exactly one invoice row for the session (counted through the product's own data registration). Needs worker and broker. |
| `tests/OpenCsms.Suite/Messaging/RabbitMqEventPublisherTests.cs` | The publisher's recovery (R1a-03): a channel whose connection was closed out from under the publisher is not reused; the next publish opens a new connection and reaches the exchange. Needs the broker only. |
| `tests/OpenCsms.Domain.Tests/InvoiceCalculatorTests.cs` | Tariff math: energy at the tariff price, the start fee once per session, per-component rounding away from zero, and the idle rule — idle = stop − last meter value − grace, billed at `ceil(hours)` only when strictly positive: within the grace period, exactly at the grace boundary and one second past its edge (one started hour), every started hour beyond it, rounding up to the started hour, and the idle fee's own money rounding. Plus the rejection rules (open session, foreign tariff, regressing meter value, meter after the session ended, end before the last meter value). |

The suite runs its tests in parallel (`tests/OpenCsms.Suite/NUnitParallelization.cs`); every test
owns its tenant, tariff and station (and its charge point identity, for the OCPP journeys), and the
messaging awaits match by test-owned ids, so the shared containers, API and worker are the only shared
state; the idle-fee journeys advance their own per-test clock, seeded from the run, so advanced time
never leaks into a concurrent test. Per-test prerequisites go through the product's REST front door
with the mechanics on `ProtoTest.Data` provisioners — input (`RegisterTariffRequest`,
`RegisterStationRequest`, `CreateUserRequest`) → route (`POST /api/tariffs`, `/api/stations`,
`/api/users`) → typed result (`TariffResponse`, `StationResponse`, `UserResponse`) — orchestrated
by `CsmsProvisioning` for the operator attribute, the neighbor tenants, the viewers and the
contract tests; rows are never deleted (the API has no delete route), so uniqueness comes from the
per-test names. The seeded busy month is shared read-only state: two tenants
with 120 May-2030 invoices each, stored once per run by the `seeded-month` run setup step
(`tests/OpenCsms.Suite/Support/SeededMonth.cs`) from the product's own application services, with
the seeded tariff's name as the idempotency marker a rerun looks for. Container mode is verified by
two R3.4 runs with fresh Testcontainers (64/64, including the seven Chromium journeys;
`artifacts/gates/opencsms-container-20260926-234242.log` and
`artifacts/gates/opencsms-container-20260926-234313.log`) and configured mode by two R3.2 runs against one docker-run PostgreSQL and RabbitMQ that outlives
them (57/57 twice; `artifacts/gates/opencsms-configured-20260926-230155.log` and
`artifacts/gates/opencsms-configured-20260926-230216.log`; the second run reused the first run's
seed, which is the idempotency proof).
Both modes' output is recorded under
`artifacts/gates/opencsms-<mode>-<timestamp>.log` by `eng/run-suite.ps1`, which builds the dashboard
first so the browser tests always run a fresh bundle.

The schema migration from M1 to M2 was verified on a database that already held stations and sessions:
the new `ChargePointId` column is added nullable, backfilled from each station's own id (no duplicate
keys), and only then made required and unique, while `TransactionId` is an identity column that fills
existing sessions from its sequence. R2.3's `Sessions.MeterStartKwh` column defaults to zero, which is
the baseline sessions written before it implicitly had. R3.1's `Users` table is a new table
(`DashboardUsers`), so its migration adds rows to no existing data.

Every run writes its evidence under `TestResults/OpenCsms/` beside the built test assembly:
`opencsms.prototrace`, `report.json` and `report.html`, with REST route coverage collected by
`RestCoverageCollector`.

## Not tested (the gaps)

- **The rest of OCPP 1.6J beyond the named refusals** — the actions the README names (Authorize,
  DataTransfer, Reset, UnlockConnector, UpdateFirmware, the firmware/diagnostics status messages,
  smart-charging profiles) are each pinned as refused in `OcppErrorPaths`; any other message the CSMS
  does not implement falls to the same default branch but is not individually asserted.
- **An operator remote start racing a reconnect** — the registry replaces the old connection
  when the new one registers, but no test drives the call against the race itself yet.
- **The device coverage report** — `OcppProtocol` classifies frames for the trace
  (`device.frame.kind`), but no `DeviceCoverageCollector` is registered; see the note below.
- **Operator dashboard and public status page** — R3.2 ships the depth: the session timeline
  with invoice links, the invoice calculation lines, tariff repricing for the operator admin with
  an honest viewer UI, the monthly `.xlsx` export with Sheets assertions, the viewer-restriction
  browser journey and the multi-tenancy negative test. R3.4 closes the two opens: the export
  download button is clicked in Chromium with the downloaded bytes asserted as real cells, and the
  remote-stop button drives a connected charge point from the station screen (through the
  dashboard application's own device client, over a real socket to the loopback listener).
- **Monthly `.xlsx` export** — M3, R3.2: done (above).
- **Published/deployed mode** — planned M4; only the configuration shape is known.
- **Multi-tenancy negative** — R3.2: `TenantIsolation` pins the dashboard reads and the
  remote-stop command in both directions (plus seeded-tenant against seeded-tenant), and
  `TenantIsolationJourney` pins the stations list and the foreign station screen in the browser.
- **Payments / PSP notifications** — not built.

## Recorded, accepted for now

- **The management API is unauthenticated until M4.** `/api/tariffs`, `/api/stations`,
  `/api/sessions`, `/api/users` and the machine remote commands are the device/operator machine
  surface with no credentials yet (R3.1); the dashboard's reads, its tariff repricing, its remote
  commands and the monthly export are cookie-authenticated, tenant-scoped and (for the mutations)
  role-checked, with the cookie `SameSite=Lax` and anti-forgery tokens on the mutations since R3.2.
  The SPA's sign-in surface is the product's own `/api/auth/sign-in`.
- **The OCPP catalog drives traces, not the coverage report (framework finding, R2.1).** A call
  result echoes only the message id, so the device framework's frame-only `Classify` cannot attribute
  a matched result to the action that caused it; registering `DeviceCoverageCollector` with the eight
  actions would report every client-side action as an uncovered gap even though the suite asserts all
  of them. The catalog is therefore registered on the client for trace classification only, and the
  subset table in the README is the coverage statement. Revisit when a device protocol can correlate a
  request with its response.
- **At-most-once `session.ended`.** `/api/sessions/{id}/end` commits the ended session before it
  publishes (`src/OpenCsms.Api/Program.cs`), so a publish failure leaves the session ended with no
  event: the client gets a 500, a retry answers 409, and the invoice is never produced. Accepted for
  M1; a transactional outbox (or a retry) is scheduled with M4's fault-injection work (R1a-02). The
  OCPP StopTransaction path shares this behavior, because it goes through the same session-ending
  helper.
- **Every test connects to the broker at setup.** The run-wide `Tap(CsmsEvents.Exchange)` pre-bind in
  `tests/OpenCsms.Suite/Setup.cs` makes the tap prepare its destination during test setup,
  including for the REST-only contract tests; a broker-less run therefore fails setup there rather
  than skipping. The broker capability gates the tests that need it; the pre-bind is suite-wide by
  design (R1a-13).
