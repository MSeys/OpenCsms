[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("container", "configured")]
    [string]$Mode
)

# One command, one log: the suite's two modes are M1's evidence, so each run writes
# artifacts/gates/opencsms-<mode>-<timestamp>.log the way ProtoTest's verify.ps1 writes its gate
# records, and a plan row can cite a file instead of prose (R1a-05). The mode names the evidence;
# the environment decides which containers the suite skips.

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$gatesRoot = Join-Path $repository "artifacts/gates"
New-Item -ItemType Directory -Path $gatesRoot -Force | Out-Null

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

$logPath = Join-Path $gatesRoot ("opencsms-{0}-{1}.log" -f $Mode, (Get-Date).ToString("yyyyMMdd-HHmmss"))
Write-Host "=== opencsms ${Mode}: dotnet test tests/OpenCsms.Suite -c Release -> $logPath ==="
Write-Host ""

$exitCode = 1
Push-Location $repository
try {
    dotnet test tests/OpenCsms.Suite -c Release --nologo 2>&1 | Tee-Object -FilePath $logPath
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

$summary = @(Get-Content -LiteralPath $logPath | Where-Object { $_ -match '^\s*(Passed!|Failed!)' })
if ($summary.Count -gt 0) {
    Write-Host ""
    Write-Host $summary[-1].Trim()
}
Write-Host "evidence: $logPath"

exit $exitCode
