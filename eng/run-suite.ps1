[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("container", "configured", "published", "topology")]
    [string]$Mode
)

# One command, one log: the suite's modes are its evidence, so each run writes
# artifacts/gates/opencsms-<mode>-<timestamp>.log the way ProtoTest's verify.ps1 writes its gate
# records, so a result can cite a file instead of prose. The mode names the evidence;
# the environment decides which containers the suite skips. Topology selects the AppHost with the
# integration's one key and lets the AppHost's project resources serve the whole product; container
# and configured leave the key unset, so the in-process/container chain wins. Published starts the
# local rehearsal stack itself - the API and both workers as real processes against the running
# opencsms-postgres/opencsms-rabbitmq containers - and exports the five keys that point the suite
# at it, so the suite's own server and worker hosts step aside.

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$gatesRoot = Join-Path $repository "artifacts/gates"
New-Item -ItemType Directory -Path $gatesRoot -Force | Out-Null
$timestamp = (Get-Date).ToString("yyyyMMdd-HHmmss")
$publishedProcesses = @()

function Start-RehearsalProcess {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    $log = Join-Path $gatesRoot ("opencsms-published-{0}-{1}.log" -f $timestamp, $Name)
    $errors = Join-Path $gatesRoot ("opencsms-published-{0}-{1}.error.log" -f $timestamp, $Name)
    $process = Start-Process -FilePath "dotnet" -ArgumentList $Arguments `
        -WorkingDirectory $repository -NoNewWindow -PassThru `
        -RedirectStandardOutput $log -RedirectStandardError $errors
    $process | Add-Member -NotePropertyName ProcessLog -NotePropertyValue $log
    Write-Host "  ${Name}: pid $($process.Id) -> $log"
    return $process
}

function Stop-RehearsalProcess {
    param([Parameter(Mandatory)]$Process)

    if ($Process.HasExited) {
        return
    }

    # The product runs inside the dotnet process itself; taskkill also takes a child a run left.
    if ($IsWindows) {
        & taskkill /PID $Process.Id /T /F 2>$null | Out-Null
    }
    else {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    }

    $Process.WaitForExit(10000) | Out-Null
}

if ($Mode -eq "configured") {
    $required = @(
        "ConnectionStrings__Csms",
        "ProtoTest__Messaging__RabbitMq__ConnectionString",
        "Messaging__RabbitMq__ConnectionString"
    )
    $missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
    if ($missing.Count -gt 0) {
        throw "Configured mode needs the three exported keys; missing: $($missing -join ', ')."
    }
}

if ($Mode -eq "topology") {
    # The AppHost's own resources serve the store and the broker, so the run must not inherit a
    # published environment's keys from the shell: clear what a configured rehearsal exports, and
    # select the AppHost with the integration's one key.
    foreach ($key in @(
        "ConnectionStrings__Csms",
        "Messaging__RabbitMq__ConnectionString",
        "ProtoTest__Messaging__RabbitMq__ConnectionString",
        "ProtoTest__Applications__Csms__BaseUrl",
        "ProtoTest__Applications__Dashboard__BaseUrl"
    )) {
        Remove-Item "Env:$key" -ErrorAction SilentlyContinue
    }

    $env:ProtoTest__Aspire__Enabled = "true"
}

if ($Mode -eq "published") {
    # The rehearsal stack is persistent and started outside the suite: the containers keep one
    # database across runs. A missing container is a setup error, not something to paper over with
    # a fresh one, so the check names the stack a developer has to start once.
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw "Published mode needs the running opencsms-postgres/opencsms-rabbitmq containers; docker was not found."
    }

    foreach ($container in @("opencsms-postgres", "opencsms-rabbitmq")) {
        $running = & docker inspect --format "{{.State.Running}}" $container 2>$null
        if ($LASTEXITCODE -ne 0 -or "$running".Trim() -ne "true") {
            throw @"
Published mode needs the running '$container' container. Start the rehearsal stack once with:
  docker run -d --name opencsms-postgres -e POSTGRES_USER=opencsms -e POSTGRES_PASSWORD=opencsms -e POSTGRES_DB=opencsms -p 5432:5432 postgres:16-alpine
  docker run -d --name opencsms-rabbitmq -p 5672:5672 rabbitmq:3-alpine
"@
        }
    }

    $listening = [System.Net.Sockets.TcpClient]::new()
    try {
        $listening.Connect("127.0.0.1", 5080)
        throw "Port 5080 already answers. Stop the process listening there, then run published mode again."
    }
    catch [System.Net.Sockets.SocketException] {
        # Nothing answers: the port is free for the rehearsal API.
    }
    finally {
        $listening.Dispose()
    }

    # A shell may carry keys from another leg (the AppHost selector, a staging target, a seed
    # switch); the rehearsal fixes its own environment so the same command measures the same stack.
    Remove-Item Env:ProtoTest__Aspire__Enabled, Env:ProtoTest__Seed,
        Env:Notifications__InvoiceReadyBaseUrl, Env:Notifications__BillingFailureBaseUrl `
        -ErrorAction SilentlyContinue

    $env:ConnectionStrings__Csms = "Host=localhost;Database=opencsms;Username=opencsms;Password=opencsms"
    $env:Messaging__RabbitMq__ConnectionString = "amqp://guest:guest@localhost:5672"
    $env:ProtoTest__Messaging__RabbitMq__ConnectionString = "amqp://guest:guest@localhost:5672"
    $env:ProtoTest__Applications__Csms__BaseUrl = "http://127.0.0.1:5080"
    $env:ProtoTest__Applications__Dashboard__BaseUrl = "http://127.0.0.1:5080"
}

Write-Host "=== opencsms ${Mode}: building the dashboard first ==="
& (Join-Path $PSScriptRoot "build-dashboard.ps1")
if ($LASTEXITCODE -ne 0) {
    throw "The dashboard build failed with exit code $LASTEXITCODE; the suite was not run."
}

$logPath = Join-Path $gatesRoot ("opencsms-{0}-{1}.log" -f $Mode, $timestamp)
$testArguments = @("test", "tests/OpenCsms.Suite", "-c", "Release", "--nologo")
$exitCode = 1

try {
    if ($Mode -eq "published") {
        Write-Host "=== opencsms published: building the product the rehearsal stack runs ==="
        dotnet build tests/OpenCsms.Suite -c Release --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Building the suite's product references failed with exit code $LASTEXITCODE; the stack was not started."
        }

        Write-Host "=== opencsms published: starting the API and both workers ==="
        $publishedProcesses += Start-RehearsalProcess -Name "api" -Arguments @(
            "src/OpenCsms.Api/bin/Release/net8.0/OpenCsms.Api.dll",
            "--urls", "http://127.0.0.1:5080",
            # The host resolves a relative content root against the app's base directory, so the
            # dashboard path the API serves ('../OpenCsms.Dashboard/dist') needs the absolute one.
            "--contentRoot", (Join-Path $repository "src/OpenCsms.Api"))
        $publishedProcesses += Start-RehearsalProcess -Name "billing-worker" -Arguments @(
            "src/OpenCsms.Billing.Worker/bin/Release/net8.0/OpenCsms.Billing.Worker.dll")
        $publishedProcesses += Start-RehearsalProcess -Name "notification-worker" -Arguments @(
            "src/OpenCsms.Notification.Worker/bin/Release/net8.0/OpenCsms.Notification.Worker.dll")

        Write-Host "=== opencsms published: waiting for http://127.0.0.1:5080/healthz ==="
        $deadline = (Get-Date).AddSeconds(60)
        $healthy = $false
        while ((Get-Date) -lt $deadline) {
            if ($publishedProcesses[0].HasExited) {
                throw "The API process exited with code $($publishedProcesses[0].ExitCode); its log is $($publishedProcesses[0].ProcessLog)."
            }

            try {
                $probe = Invoke-WebRequest -Uri "http://127.0.0.1:5080/healthz" -TimeoutSec 5
                if ($probe.StatusCode -eq 200) {
                    $healthy = $true
                    break
                }
            }
            catch {
                Start-Sleep -Milliseconds 500
            }
        }

        if (-not $healthy) {
            throw "The API did not answer /healthz within 60 seconds; its log is $($publishedProcesses[0].ProcessLog)."
        }

        # The product assemblies are the running processes' own files; the suite runs the build
        # above as-is instead of rewriting files a live process holds.
        $testArguments += "--no-build"
    }

    Write-Host "=== opencsms ${Mode}: dotnet $($testArguments -join ' ') -> $logPath ==="
    Write-Host ""

    Push-Location $repository
    try {
        dotnet @testArguments 2>&1 | Tee-Object -FilePath $logPath
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}
finally {
    foreach ($process in $publishedProcesses) {
        Stop-RehearsalProcess -Process $process
    }
}

$summary = @(Get-Content -LiteralPath $logPath | Where-Object { $_ -match '^\s*(Passed!|Failed!)' })
if ($summary.Count -gt 0) {
    Write-Host ""
    Write-Host $summary[-1].Trim()
}

if ($Mode -eq "published") {
    foreach ($process in $publishedProcesses) {
        Write-Host "process log: $($process.ProcessLog)"
    }

    # The workers have no configured target here, so their idle lines are the honest note that the
    # published stack accepted the run without sending anything to the outside world.
    $idle = @($publishedProcesses |
        Where-Object { $_.ProcessLog -like "*notification-worker*" } |
        ForEach-Object { Select-String -LiteralPath $_.ProcessLog -Pattern " is idle: " -ErrorAction SilentlyContinue } |
        Select-Object -First 2)
    foreach ($line in $idle) {
        Write-Host "notification worker: $($line.Line.Trim())"
    }
}

Write-Host "evidence: $logPath"

exit $exitCode
