# Coverage — what the suite asserts today

The honest scope of the M1 suite, kept in step with the tests. Linked from the README; update it in
the same commit as the behavior it describes.

## Tested

| Where | What it asserts |
| --- | --- |
| `tests/OpenCsms.Suite/Journeys/ChargingSessionsBecomeInvoices.cs` | The M1 journey: the provisioned operator starts a session over REST, records 22 kWh and ends it; the billing worker consumes `session.ended` and publishes `invoice.issued`, which the suite awaits on the product exchange; the stored invoice is read back over REST, and the amounts are checked (energy 8.80 + start fee 1.50 = 10.30 EUR, no idle time). |
| `tests/OpenCsms.Suite/Api/SessionContracts.cs` | Synchronous REST contracts: a connector outside the station answers 400; an unknown session has no invoice (404). No worker or broker needed. |
| `tests/OpenCsms.Suite/Billing/DeadLetterTests.cs` | The worker's negative path, through the raw broker: a `session.ended` for an unknown session (published on the product exchange with the product routing key) is retried three times and dead-lettered; the test consumes `billing.session-ended.dlq` within a bounded wait and asserts the payload session id and the `x-opencsms-attempts: 3` header. Needs worker and broker, no REST. |
| `tests/OpenCsms.Domain.Tests/InvoiceCalculatorTests.cs` | Tariff math: energy at the tariff price, the start fee once per session, per-component rounding away from zero, idle fees within the grace period and per started hour beyond it, and the rejection rules (open session, foreign tariff, regressing meter value, meter after the session ended, end before the last meter value). |

The suite runs its tests in parallel (`tests/OpenCsms.Suite/NUnitParallelization.cs`); every REST test
owns its tenant, tariff and station, and the messaging awaits match by test-owned ids, so the shared
containers, API and worker are the only shared state. Container mode is verified by two runs with fresh
Testcontainers, and configured mode by two runs against one docker-run PostgreSQL and RabbitMQ that
outlives them (4/4 in every run; the second configured run coexists with the first run's persisted
tenants, tariffs and stations, which is REF-1's proof).

Every run writes its evidence under `tests/OpenCsms.Suite/bin/Debug/net8.0/TestResults/OpenCsms/`:
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
