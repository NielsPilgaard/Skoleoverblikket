#Requires -Version 7
<#
.SYNOPSIS
    Brings the live realm's login settings in line with the realm file, so they aren't clicked by hand.

.DESCRIPTION
    Keycloak imports infrastructure/keycloak/realms/Skoleoverblikket-realm.json only into an empty
    database, so a change to it never reaches production by itself. This script reads the file, applies
    the same production change as the Keycloak Dockerfile (the "Superadmin requires two-factor"
    sub-flows are on), compares it with the live realm and fixes what differs:

      - password policy, OTP policy and WebAuthn policy
      - required actions: registered, enabled or disabled, and their config (max_auth_age)
      - event listeners (only adds; never removes one)
      - our own authentication flows (the ones the file has that Keycloak doesn't ship): created when
        missing, rebuilt when they differ
      - which flow is bound as browser flow, direct grant flow and so on

    Everything else (SMTP, clients, users, themes) is left alone.

    Without -Apply it only prints what it would change. Run it once without, read the list, then run
    it again with -Apply. A second run should report everything as ok.

    Rebuilding a flow that is in use binds Keycloak's built-in flow for a few seconds, then binds the
    new one. A login in that moment goes through the built-in flow (password, plus the app if set up).

    Run it through the tunnel to the admin console (docs/DEPLOYMENT.md):
        ssh -L 8180:127.0.0.1:8180 ubuntu@<vps>
        pwsh scripts/sync-keycloak-auth.ps1 -AdminUser <your master admin>
        pwsh scripts/sync-keycloak-auth.ps1 -AdminUser <your master admin> -Apply

.PARAMETER KeycloakBase
    Keycloak's admin address. Default: http://localhost:8180 (the SSH tunnel).

.PARAMETER AdminUser
    Your master realm admin. The script asks for the password and the app code.

.PARAMETER RealmFile
    Default: the realm file in this repo.

.PARAMETER Apply
    Make the changes. Without it, nothing is changed.
#>

param(
    [string]$KeycloakBase = "http://localhost:8180",
    [Parameter(Mandatory)][string]$AdminUser,
    # Asked for when left out. Only for tests against a scratch Keycloak: a password here lands in shell history.
    [string]$Password,
    [string]$Otp,
    [string]$Realm = "Skoleoverblikket",
    [string]$RealmFile = (Join-Path $PSScriptRoot ".." "infrastructure" "keycloak" "realms" "Skoleoverblikket-realm.json"),
    [switch]$Apply
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$base = $KeycloakBase.TrimEnd('/')
$admin = "$base/admin/realms/$Realm"
$file = Get-Content -Raw -LiteralPath $RealmFile | ConvertFrom-Json -AsHashtable

# Same change as the jq step in infrastructure/keycloak/Dockerfile: the dev realm leaves these off.
foreach ($flow in $file.authenticationFlows) {
    foreach ($execution in $flow.authenticationExecutions) {
        if ($execution['flowAlias'] -and $execution.flowAlias.StartsWith("Superadmin requires two-factor")) {
            $execution.requirement = "CONDITIONAL"
        }
    }
}

# ── Admin token ─────────────────────────────────────────────────────────────────
# Master realm tokens live a minute, so renew with the refresh token instead of asking for a new code
# (an app code works only once).

if (-not $Password) { $Password = Read-Host "Password for $AdminUser" -MaskInput }
if (-not $PSBoundParameters.ContainsKey("Otp")) { $Otp = Read-Host "App code for $AdminUser (empty if none)" }
$body = @{ grant_type = "password"; client_id = "admin-cli"; username = $AdminUser; password = $Password }
if ($Otp) { $body.totp = $Otp }
$tokens = Invoke-RestMethod -Method Post -Uri "$base/realms/master/protocol/openid-connect/token" -Body $body
$Password = $null
$tokenExpires = [DateTime]::UtcNow.AddSeconds([int]$tokens.expires_in - 15)

function Get-Headers {
    if ([DateTime]::UtcNow -ge $script:tokenExpires) {
        $script:tokens = Invoke-RestMethod -Method Post -Uri "$base/realms/master/protocol/openid-connect/token" -Body @{
            grant_type = "refresh_token"; client_id = "admin-cli"; refresh_token = $script:tokens.refresh_token
        }
        $script:tokenExpires = [DateTime]::UtcNow.AddSeconds([int]$script:tokens.expires_in - 15)
    }
    @{ Authorization = "Bearer $($script:tokens.access_token)" }
}

function Invoke-Admin([string]$Method, [string]$Path, $Body = $null) {
    $params = @{ Method = $Method; Uri = "$admin/$Path"; Headers = (Get-Headers) }
    if ($null -ne $Body) {
        $params.Body = ConvertTo-Json -InputObject $Body -Depth 20 -Compress
        $params.ContentType = "application/json; charset=utf-8"
    }
    # Invoke-RestMethod passes a JSON array on as one object; writing it from a variable unrolls it.
    $result = Invoke-RestMethod @params
    $result
}

function Enc([string]$s) { [uri]::EscapeDataString($s) }

# Keycloak leaves authenticationFlow out of an execution that isn't a sub-flow.
function Test-SubFlow($e) { [bool]($e.PSObject.Properties['authenticationFlow'] -and $e.authenticationFlow) }

# ── Report ──────────────────────────────────────────────────────────────────────

$pending = 0
function Report([string]$State, [string]$What, [string]$Detail = "") {
    if ($State -ne "ok") { $script:pending++ }
    $color = @{ ok = "DarkGray"; change = "Yellow"; create = "Yellow"; rebuild = "Yellow"; register = "Yellow" }[$State]
    $line = "  [{0,-8}] {1}" -f $State, $What
    if ($Detail) { $line += " - $Detail" }
    Write-Host $line -ForegroundColor $color
}

function Same($a, $b) {
    (ConvertTo-Json -InputObject $a -Depth 10 -Compress) -eq (ConvertTo-Json -InputObject $b -Depth 10 -Compress)
}

# ── Policies ────────────────────────────────────────────────────────────────────

Write-Host "Policies"
$live = Invoke-Admin GET ""
$policyKeys = $file.Keys | Where-Object { $_ -eq "passwordPolicy" -or $_.StartsWith("otpPolicy") -or $_.StartsWith("webAuthnPolicy") } | Sort-Object
$realmUpdate = @{}
foreach ($key in $policyKeys) {
    $liveValue = if ($live.PSObject.Properties[$key]) { $live.$key } else { $null }
    $want = $file[$key]
    if (Same @($liveValue) @($want)) {
        Report ok $key
    }
    else {
        Report change $key "$(ConvertTo-Json -InputObject $liveValue -Compress) -> $(ConvertTo-Json -InputObject $want -Compress)"
        $realmUpdate[$key] = $want
    }
}
if ($Apply -and $realmUpdate.Count) { Invoke-Admin PUT "" $realmUpdate | Out-Null }

# ── Required actions ────────────────────────────────────────────────────────────

Write-Host "Required actions"
$registered = @(Invoke-Admin GET "authentication/required-actions")
foreach ($action in $file.requiredActions) {
    $alias = $action.alias
    $current = $registered | Where-Object { $_.alias -eq $alias }
    if (-not $current) {
        Report register $alias
        if ($Apply) {
            Invoke-Admin POST "authentication/register-required-action" @{ providerId = $action.providerId; name = $action.name } | Out-Null
            $current = Invoke-Admin GET "authentication/required-actions/$(Enc $alias)"
        }
    }
    elseif ($current.enabled -ne $action.enabled) {
        Report change $alias "enabled $($current.enabled) -> $($action.enabled)"
    }
    else {
        Report ok $alias
    }

    if ($current -and $current.enabled -ne $action.enabled -and $Apply) {
        $current.enabled = $action.enabled
        Invoke-Admin PUT "authentication/required-actions/$(Enc $alias)" $current | Out-Null
    }

    $wantConfig = if ($action['config']) { $action.config } else { @{} }
    if ($wantConfig.Count) {
        $liveConfig = @{}
        if ($current) {
            $cfg = Invoke-Admin GET "authentication/required-actions/$(Enc $alias)/config"
            if ($cfg -and $cfg.PSObject.Properties['config'] -and $cfg.config) {
                $cfg.config.PSObject.Properties | ForEach-Object { $liveConfig[$_.Name] = $_.Value }
            }
        }
        $differs = $wantConfig.Keys | Where-Object { "$($liveConfig[$_])" -ne "$($wantConfig[$_])" }
        if ($differs) {
            Report change "$alias config" (($differs | ForEach-Object { "$_ '$($liveConfig[$_])' -> '$($wantConfig[$_])'" }) -join ", ")
            if ($Apply) {
                # Keycloak rejects a config that leaves out a field, even an optional one (recovery codes'
                # warning_threshold), so missing fields get Keycloak's own default.
                $description = Invoke-Admin GET "authentication/required-actions/$(Enc $alias)/config-description"
                foreach ($p in @($description.properties)) {
                    if (-not $liveConfig.ContainsKey($p.name) -and $null -ne $p.defaultValue) { $liveConfig[$p.name] = "$($p.defaultValue)" }
                }
                foreach ($k in $wantConfig.Keys) { $liveConfig[$k] = $wantConfig[$k] }
                Invoke-Admin PUT "authentication/required-actions/$(Enc $alias)/config" @{ config = $liveConfig } | Out-Null
            }
        }
    }
}

# ── Event listeners ─────────────────────────────────────────────────────────────

Write-Host "Event listeners"
$events = Invoke-Admin GET "events/config"
$missing = @($file.eventsListeners | Where-Object { $_ -notin $events.eventsListeners })
if ($missing.Count) {
    Report change "eventsListeners" "add $($missing -join ', ')"
    if ($Apply) {
        $events.eventsListeners = @($events.eventsListeners) + $missing
        Invoke-Admin PUT "events/config" $events | Out-Null
    }
}
else {
    Report ok "eventsListeners"
}

# ── Flows ───────────────────────────────────────────────────────────────────────

$flowsByAlias = @{}
foreach ($flow in $file.authenticationFlows) { $flowsByAlias[$flow.alias] = $flow }
$configsByAlias = @{}
foreach ($config in $file.authenticatorConfig) { $configsByAlias[$config.alias] = $config.config }

# The file's flow as the flat list Keycloak's executions endpoint returns: depth-first, by priority.
function Get-WantedRows([string]$FlowAlias, [int]$Level = 0) {
    $sorted = $flowsByAlias[$FlowAlias].authenticationExecutions | Sort-Object { [int]$_.priority }
    foreach ($e in $sorted) {
        $config = if ($e['authenticatorConfig']) { $configsByAlias[$e.authenticatorConfig] } else { $null }
        if ($e.authenticatorFlow) {
            [pscustomobject]@{ Level = $Level; Name = "flow:$($e.flowAlias)"; Requirement = $e.requirement; Config = $null }
            Get-WantedRows $e.flowAlias ($Level + 1)
        }
        else {
            [pscustomobject]@{ Level = $Level; Name = $e.authenticator; Requirement = $e.requirement; Config = $config }
        }
    }
}

function Get-LiveRows([string]$FlowAlias) {
    foreach ($e in @(Invoke-Admin GET "authentication/flows/$(Enc $FlowAlias)/executions")) {
        $config = $null
        if ($e.PSObject.Properties['authenticationConfig'] -and $e.authenticationConfig) {
            $c = Invoke-Admin GET "authentication/config/$($e.authenticationConfig)"
            $config = @{}
            $c.config.PSObject.Properties | ForEach-Object { $config[$_.Name] = $_.Value }
        }
        $name = if (Test-SubFlow $e) { "flow:$($e.displayName)" } else { $e.providerId }
        [pscustomobject]@{ Level = [int]$e.level; Name = $name; Requirement = $e.requirement; Config = $config }
    }
}

function Format-Row($r) {
    if (-not $r) { return "(nothing)" }
    $s = "$('  ' * $r.Level)$($r.Name) $($r.Requirement)"
    if ($r.Config) { $s += " {" + (($r.Config.Keys | Sort-Object | ForEach-Object { "$_=$($r.Config[$_])" }) -join ", ") + "}" }
    $s
}

function Find-FirstDifference($want, $have) {
    for ($i = 0; $i -lt [Math]::Max($want.Count, $have.Count); $i++) {
        $w = if ($i -lt $want.Count) { $want[$i] } else { $null }
        $h = if ($i -lt $have.Count) { $have[$i] } else { $null }
        if ((Format-Row $w) -ne (Format-Row $h)) { return "row $($i + 1): live '$((Format-Row $h).Trim())', file '$((Format-Row $w).Trim())'" }
    }
    $null
}

$usedConfigAliases = @{}
function Add-Config([string]$ExecutionId, [string]$Alias, $Config) {
    # An alias can only be used once per realm, and the file shares one config between flows.
    for ($n = 1; ; $n++) {
        $candidate = if ($n -eq 1) { $Alias } else { "$Alias-$n" }
        if ($usedConfigAliases[$candidate]) { continue }
        try {
            Invoke-Admin POST "authentication/executions/$ExecutionId/config" @{ alias = $candidate; config = $Config } | Out-Null
            $usedConfigAliases[$candidate] = $true
            return
        }
        catch {
            if ($_.Exception.Response.StatusCode -ne 409 -or $n -ge 10) { throw }
            $usedConfigAliases[$candidate] = $true
        }
    }
}

function Set-Requirement([string]$ParentAlias, [scriptblock]$Match, [string]$Requirement) {
    $row = @(Invoke-Admin GET "authentication/flows/$(Enc $ParentAlias)/executions") | Where-Object { $_.level -eq 0 } | Where-Object $Match | Select-Object -Last 1
    if (-not $row) { throw "Could not find the new execution in '$ParentAlias'" }
    if ($row.requirement -ne $Requirement) {
        $row.requirement = $Requirement
        Invoke-Admin PUT "authentication/flows/$(Enc $ParentAlias)/executions" $row | Out-Null
    }
    $row
}

function Add-Executions([string]$ParentAlias) {
    $sorted = $flowsByAlias[$ParentAlias].authenticationExecutions | Sort-Object { [int]$_.priority }
    foreach ($e in $sorted) {
        if ($e.authenticatorFlow) {
            $sub = $flowsByAlias[$e.flowAlias]
            Invoke-Admin POST "authentication/flows/$(Enc $ParentAlias)/executions/flow" @{
                alias = $sub.alias; description = $sub["description"]; provider = "registration-page-form"; type = $sub.providerId
            } | Out-Null
            Set-Requirement $ParentAlias { (Test-SubFlow $_) -and $_.displayName -eq $sub.alias } $e.requirement | Out-Null
            Add-Executions $sub.alias
        }
        else {
            Invoke-Admin POST "authentication/flows/$(Enc $ParentAlias)/executions/execution" @{ provider = $e.authenticator } | Out-Null
            $row = Set-Requirement $ParentAlias { -not (Test-SubFlow $_) -and $_.providerId -eq $e.authenticator } $e.requirement
            if ($e['authenticatorConfig']) { Add-Config $row.id $e.authenticatorConfig $configsByAlias[$e.authenticatorConfig] }
        }
    }
}

$builtInBinding = @{
    browserFlow = "browser"; registrationFlow = "registration"; directGrantFlow = "direct grant"
    resetCredentialsFlow = "reset credentials"; clientAuthenticationFlow = "clients"
    dockerAuthenticationFlow = "docker auth"; firstBrokerLoginFlow = "first broker login"
}

Write-Host "Flows"
$liveFlows = @(Invoke-Admin GET "authentication/flows")
foreach ($flow in $file.authenticationFlows | Where-Object { $_.topLevel -and -not $_.builtIn }) {
    $alias = $flow.alias
    $existing = $liveFlows | Where-Object { $_.alias -eq $alias }
    $want = @(Get-WantedRows $alias)

    if ($existing) {
        $difference = Find-FirstDifference $want @(Get-LiveRows $alias)
        if (-not $difference) { Report ok $alias; continue }
        Report rebuild $alias $difference
        if (-not $Apply) { continue }

        # Keycloak won't delete a bound flow: bind its built-in one for the moment.
        $live = Invoke-Admin GET ""
        $unbind = @{}
        foreach ($key in $builtInBinding.Keys) { if ($live.$key -eq $alias) { $unbind[$key] = $builtInBinding[$key] } }
        if ($unbind.Count) { Invoke-Admin PUT "" $unbind | Out-Null }
        Invoke-Admin DELETE "authentication/flows/$($existing.id)" | Out-Null
    }
    else {
        Report create $alias
        if (-not $Apply) { continue }
    }

    Invoke-Admin POST "authentication/flows" @{
        alias = $alias; description = $flow["description"]; providerId = $flow.providerId; topLevel = $true; builtIn = $false
    } | Out-Null
    Add-Executions $alias

    $difference = Find-FirstDifference $want @(Get-LiveRows $alias)
    if ($difference) { throw "Built '$alias', but it still differs from the file: $difference" }
}

# ── Bindings ────────────────────────────────────────────────────────────────────

Write-Host "Bindings"
$live = Invoke-Admin GET ""
$bind = @{}
foreach ($key in $builtInBinding.Keys | Sort-Object) {
    if (-not $file[$key]) { continue }
    if ($live.$key -eq $file[$key]) { Report ok "$key = $($file[$key])"; continue }
    Report change $key "'$($live.$key)' -> '$($file[$key])'"
    $bind[$key] = $file[$key]
}
if ($Apply -and $bind.Count) { Invoke-Admin PUT "" $bind | Out-Null }

Write-Host ""
if ($pending -eq 0) {
    Write-Host "Everything matches the realm file." -ForegroundColor Green
}
elseif ($Apply) {
    Write-Host "Applied $pending change(s). Run again without -Apply to check: it should report everything as ok." -ForegroundColor Green
}
else {
    Write-Host "$pending change(s) to make. Nothing was changed; run again with -Apply." -ForegroundColor Yellow
}
