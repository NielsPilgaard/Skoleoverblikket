#Requires -Version 5.1
<#
.SYNOPSIS
    Sets up the local development environment for Skoleoverblikket.
.DESCRIPTION
    Installs .NET 10, Node.js (LTS), Aspire CLI, and verifies Docker is available.
    Skips any tool that is already installed at the required version.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step($message) {
    Write-Host "`n=> $message" -ForegroundColor Cyan
}

function Write-Skip($message) {
    Write-Host "   [SKIP] $message" -ForegroundColor DarkGray
}

function Write-Ok($message) {
    Write-Host "   [OK]   $message" -ForegroundColor Green
}

function Write-Fail($message) {
    Write-Host "   [FAIL] $message" -ForegroundColor Red
}

# ── .NET 10 ──────────────────────────────────────────────────────────────────

Write-Step "Checking .NET SDK 10..."

$dotnetVersion = $null
try { $dotnetVersion = (dotnet --version 2>$null) } catch {}

if ($dotnetVersion -and $dotnetVersion.StartsWith("10.")) {
    Write-Skip ".NET SDK $dotnetVersion is already installed"
}
else {
    Write-Step "Installing .NET SDK 10 via winget..."
    winget install Microsoft.DotNet.SDK.10 --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "Failed to install .NET 10. Install manually: https://dot.net/download"
        exit 1
    }
    # Refresh PATH for current session
    $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH", "User")
    Write-Ok ".NET SDK 10 installed"
}

# ── Node.js (LTS) ───────────────────────────────────────────────────────────

Write-Step "Checking Node.js..."

$nodeVersion = $null
try { $nodeVersion = (node --version 2>$null) } catch {}

if ($nodeVersion) {
    Write-Skip "Node.js $nodeVersion is already installed"
}
else {
    Write-Step "Installing Node.js LTS via winget..."
    winget install OpenJS.NodeJS.LTS --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "Failed to install Node.js. Install manually: https://nodejs.org"
        exit 1
    }
    $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH", "User")
    Write-Ok "Node.js installed"
}

# ── Aspire CLI ───────────────────────────────────────────────────────────────

Write-Step "Checking Aspire CLI..."

$aspireInstalled = $false
try {
    $aspireHelp = aspire --version 2>$null
    if ($aspireHelp) { $aspireInstalled = $true }
}
catch {}

if ($aspireInstalled) {
    Write-Skip "Aspire CLI is already installed ($aspireHelp)"
}
else {
    Write-Step "Installing Aspire CLI..."
    Invoke-RestMethod -Uri "https://aspire.dev/install.ps1" | Invoke-Expression
    $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH", "User")
    $aspireCheck = $null
    try { $aspireCheck = (aspire --version 2>$null) } catch {}
    if (-not $aspireCheck) {
        Write-Fail "Failed to install Aspire CLI. Run manually: irm https://aspire.dev/install.ps1 | iex"
        exit 1
    }
    Write-Ok "Aspire CLI installed"
}

# ── ryni (Markdown/skill linter, used by verify.ps1) ────────────────────────

Write-Step "Checking ryni..."

$ryniVersion = $null
try { $ryniVersion = (ryni --version 2>$null) } catch {}

if ($ryniVersion) {
    Write-Skip "ryni is already installed ($ryniVersion)"
}
else {
    Write-Step "Installing ryni..."
    Invoke-RestMethod -Uri "https://github.com/computerlovetech/ryni/releases/download/v0.2.2/ryni-installer.ps1" | Invoke-Expression
    $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH", "User")
    $ryniCheck = $null
    try { $ryniCheck = (ryni --version 2>$null) } catch {}
    if (-not $ryniCheck) {
        Write-Fail "Failed to install ryni. See https://github.com/computerlovetech/ryni#installation"
        exit 1
    }
    Write-Ok "ryni installed"
}

# ── Web dependencies + Playwright browser ───────────────────────────────────
# npm install also enables the .githooks pre-commit formatter. The Playwright browser
# version is pinned by @playwright/test, so it must be re-downloaded after every bump;
# `playwright install` is a no-op when the right build is already there.

Write-Step "Installing web dependencies and Playwright chromium..."

$webDir = Join-Path $PSScriptRoot '..\web'
Push-Location $webDir
try {
    npm install --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "npm install failed in web/"
        exit 1
    }
    npx playwright install chromium
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "Failed to install Playwright chromium. Run manually: cd web; npx playwright install chromium"
        exit 1
    }
    Write-Ok "Web dependencies and Playwright chromium installed"
}
finally {
    Pop-Location
}

# ── Docker ───────────────────────────────────────────────────────────────────

Write-Step "Checking Docker..."

$dockerVersion = $null
try { $dockerVersion = (docker --version 2>$null) } catch {}

if ($dockerVersion) {
    Write-Skip "Docker is already installed: $dockerVersion"

    # Check if Docker daemon is running
    $dockerRunning = $false
    try {
        docker info 2>$null | Out-Null
        $dockerRunning = $true
    }
    catch {}

    if ($dockerRunning) {
        Write-Ok "Docker daemon is running"
    }
    else {
        Write-Fail "Docker is installed but the daemon is not running. Start Docker Desktop and re-run this script."
        exit 1
    }
}
else {
    Write-Fail "Docker is not installed. Install Docker Desktop from https://www.docker.com/products/docker-desktop/"
    exit 1
}

# ── Summary ──────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Development environment is ready!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host " Start the Aspire stack:"
Write-Host "   aspire run" -ForegroundColor Yellow
Write-Host ""
