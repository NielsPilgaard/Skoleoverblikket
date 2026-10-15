#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates the external volumes of docker-compose.prod.yml on the VPS, so the selfhosted-db profile
    can start. Safe to run any number of times: a volume that exists is left alone, data and all.
    The names come from the compose file itself, so they can't be mistyped. See docs/DEPLOYMENT.md.
.PARAMETER Server
    SSH target, e.g. ubuntu@203.0.113.10 or a Host alias from ~/.ssh/config.
    Defaults to $env:BACKUP_CONSOLE_SERVER, the same server scripts/backup-console.ps1 uses.
.EXAMPLE
    pwsh scripts/create-prod-volumes.ps1 ubuntu@203.0.113.10
#>
param(
    [string]$Server = $env:BACKUP_CONSOLE_SERVER
)

$ErrorActionPreference = 'Stop'
$Compose = Join-Path (Split-Path $PSScriptRoot -Parent) 'infrastructure/docker/docker-compose.prod.yml'

if (-not $Server) {
    Write-Host "Which server? Pass it, e.g. 'pwsh scripts/create-prod-volumes.ps1 ubuntu@<vps>', or set BACKUP_CONSOLE_SERVER." -ForegroundColor Red
    exit 1
}

if (-not (Get-Command ssh -ErrorAction SilentlyContinue)) {
    Write-Host "ssh not found. Install OpenSSH (Windows: Settings > Optional features > OpenSSH Client)." -ForegroundColor Red
    exit 1
}

# The external entries of the top-level volumes: block, e.g. "  skoleoverblikket-pgdata-a:" + "    external: true".
$names = @()
$inVolumes = $false
$current = $null
foreach ($line in Get-Content $Compose) {
    if ($line -match '^\S') {
        $inVolumes = $line -match '^volumes:\s*$'
        $current = $null
    } elseif ($inVolumes -and $line -match '^  ([A-Za-z0-9_.-]+):\s*$') {
        $current = $Matches[1]
    } elseif ($inVolumes -and $current -and $line -match '^    external:\s*true\s*$') {
        $names += $current
    }
}

if ($names.Count -eq 0) {
    Write-Host "No external volumes found in $Compose. Has the file changed shape?" -ForegroundColor Red
    exit 1
}

Write-Host "Volumes in docker-compose.prod.yml: $($names -join ', ')"

# Single quotes: $v runs on the VPS. inspect first, so an existing volume is never touched.
$remote = 'for v in ' + ($names -join ' ') + '; do if docker volume inspect "$v" >/dev/null 2>&1; then echo "exists   $v"; else docker volume create "$v" >/dev/null && echo "created  $v" || exit 1; fi; done'
ssh -o ConnectTimeout=15 $Server $remote
if ($LASTEXITCODE -eq 255) {
    Write-Host "Can't SSH to $Server. Check the address and your key: ssh $Server" -ForegroundColor Red
    exit 1
}
if ($LASTEXITCODE -ne 0) {
    Write-Host "Creating a volume failed. Can this user run docker? On the VPS: docker volume ls" -ForegroundColor Red
    exit 1
}

Write-Host "All $($names.Count) volumes exist on $Server." -ForegroundColor Green
