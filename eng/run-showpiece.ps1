[CmdletBinding()]
param()

# The showpiece: the idle-fee-after-a-tariff-change journey that recorded a real regression before
# the session copied its tariff at start. The journey passes now and runs in the suite gate; this
# command runs it alone and copies its fresh trace beside the historical failing one, which stays
# untouched as the evidence the docs link to (artifacts/showpiece/opencsms.prototrace).
#
# The fresh trace lands under artifacts/showpiece/opencsms-rerun.prototrace; the run's own output is
# beside it in opencsms-showpiece.log. It starts its own PostgreSQL and RabbitMQ containers like the
# default suite run.

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repository "artifacts/showpiece"
$logPath = Join-Path $output "opencsms-showpiece.log"
New-Item -ItemType Directory -Path $output -Force | Out-Null

Write-Host "=== opencsms showpiece: the idle fee after a tariff change ==="
Write-Host ""

Push-Location $repository
try {
    dotnet test tests/OpenCsms.Suite -c Release --nologo --filter "TestCategory=Showpiece" 2>&1 |
        Tee-Object -FilePath $logPath
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

$trace = Get-ChildItem -Path (Join-Path $repository "tests/OpenCsms.Suite") -Recurse -Filter "opencsms.prototrace" -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -eq $trace) {
    throw "The showpiece ran but wrote no trace under tests/OpenCsms.Suite."
}

Copy-Item -LiteralPath $trace.FullName -Destination (Join-Path $output "opencsms-rerun.prototrace") -Force
$size = (Get-Item -LiteralPath (Join-Path $output "opencsms-rerun.prototrace")).Length
Write-Host ""
Write-Host "historical evidence: $(Join-Path $output 'opencsms.prototrace') (recorded before the fix)"
Write-Host "fresh trace: $(Join-Path $output 'opencsms-rerun.prototrace') ($size bytes)"

# A red run means the regression is back: the session no longer keeps the tariff it started under.
if ($exitCode -ne 0) {
    Write-Warning "The showpiece failed: a session no longer bills the tariff it started under."
}

exit $exitCode
