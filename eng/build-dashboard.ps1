[CmdletBinding()]
param()

# Builds the operator dashboard the API serves and the browser journeys drive: npm ci when the lock
# file is present, the install that creates it otherwise, then the same `npm run build` a developer
# runs (vue-tsc typecheck + vite build into src/OpenCsms.Dashboard/dist). The .NET build never calls
# Node; the suite's gate calls this first so the browser always tests a fresh bundle.

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$dashboard = Join-Path $repository "src/OpenCsms.Dashboard"

if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    throw "npm is required to build the dashboard; install Node.js and retry."
}

Push-Location $dashboard
try {
    $lock = Join-Path $dashboard "package-lock.json"
    if (Test-Path -LiteralPath $lock) {
        Write-Host "=== dashboard: npm ci -> $dashboard ==="
        npm ci --no-fund --no-audit
    }
    else {
        Write-Host "=== dashboard: npm install (no lock file yet) -> $dashboard ==="
        npm install --no-fund --no-audit
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Installing the dashboard dependencies failed with exit code $LASTEXITCODE."
    }

    Write-Host "=== dashboard: npm run build ==="
    npm run build
    if ($LASTEXITCODE -ne 0) {
        throw "The dashboard build failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$dist = Join-Path $dashboard "dist"
if (-not (Test-Path -LiteralPath (Join-Path $dist "index.html"))) {
    throw "The dashboard build produced no '$dist/index.html'."
}

Write-Host "dashboard build: $dist"
