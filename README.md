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
| In-process + Testcontainers | hosted by the suite (`AddAspNetCoreServer`) | started by the suite (Testcontainers) | verified: thirty-four tests green, twice, fresh containers (R2.4) |
| Configured | hosted by the suite | an environment you provide through configuration; the suite's containers skip | verified twice with the eight tests the suite had then, one persistent database (R2.1) |
| Container topology (planned) | a container | containers, `docker compose` / Aspire | M4 |
| Published (planned) | a staging URL via `ProtoTest:Applications:Csms:BaseUrl` | the staging stack | M4 |

The suite's `Setup` is the same code in both modes, and infrastructure that the environment
already provides is not started. The product resolves its broker address on first publish and its
database string when the context is first resolved, so settings that arrive after registration are
seen; the framework gap that remains (the worker's `Program.Main`) is in the gap log below.

## Status

M1 and M2 are done: the OCPP 1.6J gateway, the charge-point simulator's journey surface and
the device-fit proof are in (R2.1/R2.2), including the idle-fee journeys that advance the injected test
clock instead of sleeping, and the error paths — a duplicate StopTransaction, malformed MeterValues, an
unknown charge point, out-of-subset actions and out-of-range connectors — are pinned with the OCPP
error each one answers with (R2.3). The remote endpoints' failure branches are pinned too (R2.4): an
offline charge point is `409`, a device that refuses the call is `502`, no answer within the configured
timeout is `504`, and a device's own authorization decision (for example `Blocked`) is the `200` body's
status, not a failure. The gateway bills each session from the connector register it started at, so a
second session on a connector invoices its own energy only (R2.3). The suite is green
(`dotnet test tests/OpenCsms.Suite` → 34/34): container mode verified twice with fresh Testcontainers
(R2.4), configured mode verified twice against one persistent database when the suite had eight tests
(R2.1) — the Setup is the same code either way. The milestones below are the ones in the
[reference demo brief](https://github.com/MSeys/ProtoTest) — this README tracks them honestly.

[COVERAGE.md](COVERAGE.md) records what the suite asserts today and the areas that are still
untested.

- [x] M1 — CSMS API + PostgreSQL + billing worker + REST suite + one journey
- [x] M2 — OCPP gateway + charge-point simulator + idle-fee journey
- [ ] M3 — dashboard + monthly export + browser journeys
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
  the dashboard, the multi-tenancy negative test, payments and the `.xlsx` export have no tests yet —
  [COVERAGE.md](COVERAGE.md) lists the untested surface.

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
the console and tee it to `artifacts/gates/opencsms-<mode>-<timestamp>.log`.

`docker compose up` (M4) starts the whole topology for manual use.

## Packages

This repository consumes ProtoTest packages. While 1.1 is under development it resolves them from a
local feed produced by `eng/pack.ps1` in a sibling ProtoTest checkout (see `NuGet.config`); once 1.1 is
published it resolves them from nuget.org.

## License

MIT — see [LICENSE](LICENSE).
