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
| In-process + Testcontainers | hosted by the suite (`AddAspNetCoreServer`) | started by the suite (Testcontainers) | verified: four tests green, twice, fresh containers |
| Configured | hosted by the suite | an environment you provide through configuration; the suite's containers skip | verified: four tests green, twice, one persistent database (plan-5 1.7) |
| Container topology (planned) | a container | containers, `docker compose` / Aspire | M4 |
| Published (planned) | a staging URL via `ProtoTest:Applications:Csms:BaseUrl` | the staging stack | M4 |

The suite's `Setup` is the same code in both modes, and infrastructure that the environment
already provides is not started. The product resolves its broker address on first publish and its
database string when the context is first resolved, so settings that arrive after registration are
seen; the framework gap that remains (the worker's `Program.Main`) is in the gap log below.

## Status

M1 is in progress: the domain, the API, the billing worker and the first end-to-end journey. The suite
is green in both modes (`dotnet test tests/OpenCsms.Suite` → 4/4): in container mode with fresh
Testcontainers, and in configured mode twice against one database. The milestones below are the ones in
the [reference demo brief](https://github.com/MSeys/ProtoTest) — this README tracks them honestly.

[COVERAGE.md](COVERAGE.md) records what the suite asserts today and the areas that are still
untested.

- [ ] M1 — CSMS API + PostgreSQL + billing worker + REST suite + one journey
- [ ] M2 — OCPP gateway + charge-point simulator + idle-fee journey
- [ ] M3 — dashboard + monthly export + browser journeys
- [ ] M4 — container topology + deployed mode + fault injection + nightly CI

## Gap log

This is the honest state while M1 is in progress. The reference-suite audit and its fixes (R1a) come
before any new feature.

- The worker's `Program.Main` cannot see the run's configuration before `Build()` (ProtoTest.Hosting
  P1-gap); the product reads its connection string inside the EF options factory. The fix is Phase 2
  audit A3.
- The DLQ assertion uses a raw RabbitMQ client (`tests/OpenCsms.Suite/Support/RabbitMqRawClient.cs`):
  ProtoTest's tap binds destinations as exchanges and `ProtoMessage` drops the routing key, so a queue
  cannot be awaited through it. The `(exchange, routingKey)` addition is recorded in plan-5 (REF-5) for
  a future release; the helper carries the assertion until then.
- Published mode is planned (M4). OCPP, the dashboard, the multi-tenancy negative test, payments and
  the `.xlsx` export have no tests yet — [COVERAGE.md](COVERAGE.md) lists the untested surface.

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

`docker compose up` (M4) starts the whole topology for manual use.

## Packages

This repository consumes ProtoTest packages. While 1.1 is under development it resolves them from a
local feed produced by `eng/pack.ps1` in a sibling ProtoTest checkout (see `NuGet.config`); once 1.1 is
published it resolves them from nuget.org.

## License

MIT — see [LICENSE](LICENSE).
