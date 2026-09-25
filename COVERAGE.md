# Coverage — what the suite asserts today

The honest scope of the M1 suite, kept in step with the tests. Linked from the README; update it in
the same commit as the behavior it describes.

## Tested

| Where | What it asserts |
| --- | --- |
| `tests/OpenCsms.Suite/Journeys/ChargingSessionsBecomeInvoices.cs` | The M1 journey: the provisioned operator starts a session over REST, records 22 kWh and ends it; the billing worker consumes `session.ended` and publishes `invoice.issued`, which the suite awaits on the product exchange; the stored invoice is read back over REST, and the amounts are checked (energy 8.80 + start fee 1.50 = 10.30 EUR, no idle time). Both the event and the stored row must carry the run clock's fixed instant, so the injected `TimeProvider` is observable (R1a-04). |
| `tests/OpenCsms.Suite/Api/SessionContracts.cs` | Synchronous REST contracts: a connector outside the station answers 400; an unknown session has no invoice (404). No worker or broker needed. |
| `tests/OpenCsms.Suite/Billing/DeadLetterTests.cs` | The worker's negative path, through the raw broker: a `session.ended` for an unknown session (published on the product exchange with the product routing key) is retried three times and dead-lettered; the test consumes `billing.session-ended.dlq` within a bounded wait and asserts the payload session id and the `x-opencsms-retries: 3` header (completed retries; the fourth attempt was dead-lettered). Needs worker and broker, no REST. |
| `tests/OpenCsms.Suite/Billing/RedeliveryTests.cs` | The worker's republish-on-redelivery path (R1a-01): after the journey's setup created the invoice, the product's own `session.ended` payload is published a second time on the product exchange and routing key; `invoice.issued` must arrive again with the same invoice id, and the store must still hold exactly one invoice row for the session (counted through the product's own data registration). Needs worker and broker. |
| `tests/OpenCsms.Suite/Messaging/RabbitMqEventPublisherTests.cs` | The publisher's recovery (R1a-03): a channel whose connection was closed out from under the publisher is not reused; the next publish opens a new connection and reaches the exchange. Needs the broker only. |
| `tests/OpenCsms.Domain.Tests/InvoiceCalculatorTests.cs` | Tariff math: energy at the tariff price, the start fee once per session, per-component rounding away from zero, idle fees within the grace period and per started hour beyond it, and the rejection rules (open session, foreign tariff, regressing meter value, meter after the session ended, end before the last meter value). |

The suite runs its tests in parallel (`tests/OpenCsms.Suite/NUnitParallelization.cs`); every REST test
owns its tenant, tariff and station, and the messaging awaits match by test-owned ids, so the shared
containers, API and worker are the only shared state. Container mode is verified by two runs with fresh
Testcontainers, and configured mode by two runs against one docker-run PostgreSQL and RabbitMQ that
outlives them (6/6 in every run; the second configured run coexists with the first run's persisted
tenants, tariffs and stations, which is REF-1's proof). Both modes' output is recorded under
`artifacts/gates/opencsms-<mode>-<timestamp>.log` by `eng/run-suite.ps1`.

Every run writes its evidence under `TestResults/OpenCsms/` beside the built test assembly:
`opencsms.prototrace`, `report.json` and `report.html`, with REST route coverage collected by
`RestCoverageCollector`.

## Not tested (the gaps)

- **OCPP gateway and charge-point simulator** — M2; no device code exists yet.
- **Operator dashboard and public status page** — M3; no UI exists yet.
- **Monthly `.xlsx` export** — M3.
- **Published/deployed mode** — planned M4; only the configuration shape is known.
- **Multi-tenancy negative** — provisioning is per-test tenant, but no test asserts that one operator
  cannot read another operator's data.
- **Payments / PSP notifications** — not built.

## Recorded, accepted for now

- **At-most-once `session.ended`.** `/api/sessions/{id}/end` commits the ended session before it
  publishes (`src/OpenCsms.Api/Program.cs`), so a publish failure leaves the session ended with no
  event: the client gets a 500, a retry answers 409, and the invoice is never produced. Accepted for
  M1; a transactional outbox (or a retry) is scheduled with M4's fault-injection work (R1a-02).
- **Every test connects to the broker at setup.** The run-wide `ProtoTest:Messaging:Destinations:0`
  pre-bind in `tests/OpenCsms.Suite/Setup.cs` makes the tap prepare its destination during test setup,
  including for the REST-only contract tests; while ProtoTest's address-precedence fix (audit ADDR-1)
  is open, a broker-less run fails setup rather than skipping. Revisit the pre-bind after ADDR-1
  (R1a-13).
