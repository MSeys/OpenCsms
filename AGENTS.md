# AGENTS.md — contributor contract

OpenCSMS is a small CSMS and the reference suite for ProtoTest. The README and COVERAGE.md carry
status and coverage; this file is the short contract for working here.

## Layering

The solution is concentric and a project depends only inward:
`Domain` ← `Application` ← `Infrastructure` / `Protocol.Ocpp` / `Api` / `Billing.Worker`.
`Contracts` (integration events, API DTOs) sits on the side. Do not add a second mechanism for
something a layer already owns.

## Comments

Comments describe the code as it stands. No audit IDs, plan items, milestone/phase names or review
references in source comments — write the reason the code is what it is instead.

## Verification

- `eng/run-suite.ps1 -Mode container` runs the whole suite (it builds the dashboard first) and writes
  the evidence log to `artifacts/gates/opencsms-container-<timestamp>.log`.
- `dotnet test tests/OpenCsms.Domain.Tests -c Release` covers the domain rules; the integration suite
  is `tests/OpenCsms.Suite`.

## Provisioning in tests

Per-test prerequisites (tenant, tariff, station, account) go through the product's front door, the
REST API, so the test proves the real path, with the mechanics on `ProtoTest.Data` provisioners
(`tests/OpenCsms.Suite/Support/CsmsProvisioners.cs`, orchestrated by `CsmsProvisioning`).
Volume seeds that only arrange data for the test's subject go through the application services
in-process.

## Framework docs

The ProtoTest facts live in the ProtoTest checkout under `eng/facts/` (`architecture.md`,
`recipes.md`, `gotchas.md`). Read the owning file before changing behavior.
