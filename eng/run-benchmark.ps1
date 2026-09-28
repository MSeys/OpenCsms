[CmdletBinding()]
param(
    [int]$Iterations = 1000,
    [int]$Warmup = 100,
    [int]$Journeys = 1000
)

# One command for the published numbers: the per-test overhead against the raw WebApplicationFactory,
# the suite startup, and the seeded journey run whose trace feeds the trace-size table. The stack is
# the documented persistent rehearsal pair; the benchmark never starts containers of its own, so the
# same keys that serve `eng/run-suite.ps1 -Mode published` serve this run.
#
# Results land under artifacts/benchmarks/<timestamp>/ as results.json and results.md, with the
# journey trace beside them.

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "The benchmark needs the running opencsms-postgres/opencsms-rabbitmq containers; docker was not found."
}

foreach ($container in @("opencsms-postgres", "opencsms-rabbitmq")) {
    $running = & docker inspect --format "{{.State.Running}}" $container 2>$null
    if ($LASTEXITCODE -ne 0 -or "$running".Trim() -ne "true") {
        throw @"
The benchmark needs the running '$container' container. Start the stack once with:
  docker run -d --name opencsms-postgres -e POSTGRES_USER=opencsms -e POSTGRES_PASSWORD=opencsms -e POSTGRES_DB=opencsms -p 5432:5432 postgres:16-alpine
  docker run -d --name opencsms-rabbitmq -p 5672:5672 rabbitmq:3-alpine
"@
    }
}

# The benchmark resolves addresses from the environment, like the published leg does.
$env:ConnectionStrings__Csms = "Host=localhost;Database=opencsms;Username=opencsms;Password=opencsms"
$env:Messaging__RabbitMq__ConnectionString = "amqp://guest:guest@localhost:5672"
$env:ProtoTest__Messaging__RabbitMq__ConnectionString = "amqp://guest:guest@localhost:5672"

Write-Host "=== opencsms benchmark: $Iterations overhead iterations, $Journeys journeys ==="

Push-Location $repository
try {
    dotnet build tests/OpenCsms.Benchmarks -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Building the harness failed with exit code $LASTEXITCODE."
    }

    # A quiet machine measures honestly: the build is done, and its MSBuild nodes are not left
    # behind to compete with the run.
    dotnet build-server shutdown | Out-Null

    dotnet run -c Release --project tests/OpenCsms.Benchmarks --no-build -- `
        --mode all `
        --iterations $Iterations `
        --warmup $Warmup `
        --journeys $Journeys
    if ($LASTEXITCODE -ne 0) {
        throw "The benchmark exited with code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
