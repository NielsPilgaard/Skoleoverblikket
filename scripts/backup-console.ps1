#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Opens the backup console in one step: SSH tunnel to the VPS, the access link from the agent,
    and the browser. Ctrl+C closes the tunnel. See docs/RESTORE.md §1.
.PARAMETER Server
    SSH target, e.g. ubuntu@203.0.113.10 or a Host alias from ~/.ssh/config.
    Defaults to $env:BACKUP_CONSOLE_SERVER, so set that once and run the script with no arguments.
.PARAMETER LocalPort
    Local end of the tunnel. Change it when 9090 is taken, e.g. by the local dev stack.
.EXAMPLE
    pwsh scripts/backup-console.ps1 ubuntu@203.0.113.10
#>
param(
    [string]$Server = $env:BACKUP_CONSOLE_SERVER,
    [int]$LocalPort = 9090
)

$ErrorActionPreference = 'Stop'

if (-not $Server) {
    Write-Host "Which server? Pass it, e.g. 'pwsh scripts/backup-console.ps1 ubuntu@<vps>', or set BACKUP_CONSOLE_SERVER." -ForegroundColor Red
    exit 1
}

if (-not (Get-Command ssh -ErrorAction SilentlyContinue)) {
    Write-Host "ssh not found. Install OpenSSH (Windows: Settings > Optional features > OpenSSH Client)." -ForegroundColor Red
    exit 1
}

# Fail early when the local port is taken, instead of a tunnel that silently doesn't forward.
try {
    $probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $LocalPort)
    $probe.Start()
    $probe.Stop()
} catch {
    Write-Host "Port $LocalPort is in use (the local dev stack?). Run again with -LocalPort 9091." -ForegroundColor Red
    exit 1
}

Write-Host "Reading the access link from $Server..."
# Single quotes: $(...) runs on the VPS, not here.
$remote = 'docker exec $(docker ps -qf label=com.docker.compose.service=backup-agent) cat /var/lib/backup-agent/console-link'
$link = (ssh -o ConnectTimeout=15 $Server $remote | Select-Object -First 1)
if ($LASTEXITCODE -eq 255) {
    Write-Host "Can't SSH to $Server. Check the address and your key: ssh $Server" -ForegroundColor Red
    exit 1
}
if ($LASTEXITCODE -ne 0 -or -not $link -or $link -notmatch '^http://localhost:9090/access\?key=') {
    Write-Host "Couldn't read the link. Is the backup-agent container running? On the VPS: docker ps -a | grep backup-agent" -ForegroundColor Red
    exit 1
}
$link = $link.Trim().Replace('localhost:9090', "localhost:$LocalPort")

Write-Host "Opening the tunnel..."
$tunnel = Start-Process ssh -PassThru -NoNewWindow -ArgumentList @(
    '-N',
    '-o', 'ExitOnForwardFailure=yes',
    '-o', 'ServerAliveInterval=30',
    '-L', "${LocalPort}:127.0.0.1:9090",
    $Server
)

try {
    $ready = $false
    for ($i = 0; $i -lt 30 -and -not $tunnel.HasExited; $i++) {
        try {
            $client = [System.Net.Sockets.TcpClient]::new('127.0.0.1', $LocalPort)
            $client.Dispose()
            $ready = $true
            break
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $ready) {
        Write-Host "The tunnel didn't come up. Try by hand: ssh -L ${LocalPort}:127.0.0.1:9090 $Server" -ForegroundColor Red
        exit 1
    }

    Write-Host ""
    Write-Host "Backup console: $link" -ForegroundColor Green
    Write-Host "Opening it in your browser. Keep this window open; Ctrl+C closes the tunnel."
    Start-Process $link
    Wait-Process -Id $tunnel.Id
} finally {
    if (-not $tunnel.HasExited) {
        Stop-Process -Id $tunnel.Id -ErrorAction SilentlyContinue
    }
}
