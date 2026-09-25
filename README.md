# OpenCSMS

OpenCSMS is a small but real **EV charging management system** (CSMS) built as the **reference suite
for [ProtoTest](https://github.com/MSeys/ProtoTest)** — not as a sample inside the framework
repository.

It is deliberately a product, not a demo around a feature: a REST API, PostgreSQL, a billing worker on
RabbitMQ, and the tests that prove a charging session becomes an invoice. ProtoTest is the only test
framework. Every framework gap this app pulled is written down here rather than hidden.

## What this proves

The point is the *one suite, many environments* promise, without `if` statements in test setup:

| Mode | API | PostgreSQL & RabbitMQ | Used for |
| --- | --- | --- | --- |
| In-process | hosted by the suite (`AddAspNetCoreServer`) | started by the suite (Testcontainers) | every change |
| Configured | hosted by the suite | an environment you provide through configuration | machines without a container runtime |
| Container topology (planned) | a container | containers, `docker compose` / Aspire | merge and nightly runs |
| Published (planned) | a staging URL via `ProtoTest:Applications:Csms:BaseUrl` | the staging stack | pre-prod smoke |

The suite's `Setup` is the same code in all of them. Infrastructure that the environment already
provides is not started, and the applications' addresses are resolved at use time — that is the
ProtoTest address-provider model this repository exists to keep honest.

## Status

M1 is in progress: the domain, the API, the billing worker and the first end-to-end journey. The
milestones below are the ones in the [reference demo brief](https://github.com/MSeys/ProtoTest) —
this README tracks them honestly.

- [ ] M1 — CSMS API + PostgreSQL + billing worker + REST suite + one journey
- [ ] M2 — OCPP gateway + charge-point simulator + idle-fee journey
- [ ] M3 — dashboard + monthly export + browser journeys
- [ ] M4 — container topology + deployed mode + fault injection + nightly CI

## Running

```bash
dotnet test
```

On a machine with a container runtime the suite starts PostgreSQL and RabbitMQ itself. Without one,
provide both through configuration and the suite skips the containers:

```bash
export ConnectionStrings__Csms="Host=localhost;Database=opencsms;Username=opencsms;Password=opencsms"
export ProtoTest__Messaging__RabbitMq__ConnectionString="amqp://guest:guest@localhost:5672"
dotnet test
```

`docker compose up` (M4) starts the whole topology for manual use.

## Packages

This repository consumes ProtoTest packages. While 1.1 is under development it resolves them from a
local feed produced by `eng/pack.ps1` in a sibling ProtoTest checkout (see `NuGet.config`); once 1.1 is
published it resolves them from nuget.org.

## License

MIT — see [LICENSE](LICENSE).
