#Requires -Version 7
<#
.SYNOPSIS
    Compares Keycloak logins with the Staff, Parent and BoardMember rows in the database.

.DESCRIPTION
    Reads every user in the realm through the Keycloak admin API (the skoleoverblikket-admin
    service account) and writes a SQL file. Run that file with psql against the database. It
    lists:
      1. logins with no Staff, Parent or BoardMember row in any school
      2. logins whose tenant_id differs from the school of their rows
      3. logins whose tenant_id names no school
      4. logins holding the Keycloak admin or superadmin realm role (directly, through a group
         or through a composite role)
      5. rows whose login no longer exists in Keycloak (housekeeping only)

    Groups 1-4 are what a leaked admin client secret could leave behind: a login with no row
    still passes [Authorize]-only endpoints, and the Keycloak admin role still grants school
    admin during Phase A of task 61.

    The SQL runs in a transaction that is rolled back, so it changes nothing. The file holds
    names and emails, so it is written to the temp folder by default. Delete it afterwards.

    Run it on the server:
        Get-Content <file> | ssh <vps> "docker exec -i <postgres container> psql -U postgres -d skoleoverblikket"

.PARAMETER KeycloakBase
    Base URL of Keycloak. Default: https://auth.skoleoverblikket.dk

.PARAMETER ClientSecret
    Secret of the skoleoverblikket-admin client. Falls back to $env:KEYCLOAK_ADMIN_CLIENT_SECRET,
    then asks.

.PARAMETER OutFile
    Where to write the SQL. Default: keycloak-audit.sql in the temp folder.
#>

param(
    [string]$KeycloakBase = "https://auth.skoleoverblikket.dk",
    [string]$Realm = "Skoleoverblikket",
    [string]$ClientId = "skoleoverblikket-admin",
    [string]$ClientSecret = $env:KEYCLOAK_ADMIN_CLIENT_SECRET,
    [string]$OutFile = (Join-Path ([IO.Path]::GetTempPath()) "keycloak-audit.sql")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $ClientSecret) {
    $ClientSecret = Read-Host "Secret for $ClientId" -MaskInput
}

# A stale report from an earlier run must not pass for this one if this run fails.
Remove-Item -LiteralPath $OutFile -ErrorAction SilentlyContinue

$base = $KeycloakBase.TrimEnd('/')
$token = $null
$tokenExpires = [DateTime]::MinValue

# Access tokens live 5 minutes and a large realm takes longer to scan, so renew one 30 s before expiry.
function Get-Headers {
    if ([DateTime]::UtcNow -ge $script:tokenExpires) {
        $response = Invoke-RestMethod -Method Post -Uri "$base/realms/$Realm/protocol/openid-connect/token" -Body @{
            grant_type    = "client_credentials"
            client_id     = $ClientId
            client_secret = $ClientSecret
        }
        $script:token = $response.access_token
        $script:tokenExpires = [DateTime]::UtcNow.AddSeconds([int]$response.expires_in - 30)
    }
    @{ Authorization = "Bearer $script:token" }
}

function Get-Admin([string]$path) {
    Invoke-RestMethod -Headers (Get-Headers) -Uri "$base/admin/realms/$Realm/$path"
}

function Get-Paged([string]$path) {
    $pageSize = 100
    $all = [System.Collections.Generic.List[object]]::new()
    for ($first = 0; ; $first += $pageSize) {
        $sep = if ($path.Contains('?')) { '&' } else { '?' }
        $page = @(Get-Admin "$path${sep}first=$first&max=$pageSize")
        $all.AddRange($page)
        if ($page.Count -lt $pageSize) { return $all }
    }
}

function Get-Prop($object, [string]$name) {
    $prop = $object.PSObject.Properties[$name]
    if ($prop) { $prop.Value } else { $null }
}

function Sql([object]$value) {
    if ($null -eq $value -or $value -eq '') { return 'NULL' }
    return "'" + ([string]$value -replace "'", "''") + "'"
}

$users = @(Get-Paged "users?briefRepresentation=false" |
        Where-Object { -not $_.username.StartsWith("service-account-") })

# Effective roles, not direct mappings: admin or superadmin can also come through a group or a
# composite role. The user endpoint should already include group roles; the union with each group's
# effective roles keeps the report right even if it doesn't.
$audited = "admin", "superadmin"
$groupRoles = @{}
$realmRoles = @{}
$deleted = [System.Collections.Generic.HashSet[string]]::new()
foreach ($u in $users) {
    try {
        $mapped = @(Get-Admin "users/$($u.id)/role-mappings/realm/composite")
        $groups = @(Get-Paged "users/$($u.id)/groups")
    } catch [Microsoft.PowerShell.Commands.HttpResponseException] {
        # 404: the user was deleted after the listing.
        if ([int]$_.Exception.Response.StatusCode -ne 404) { throw }
        [void]$deleted.Add($u.id)
        continue
    }
    $names = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($role in $mapped) { [void]$names.Add($role.name) }
    foreach ($group in $groups) {
        if (-not $groupRoles.ContainsKey($group.id)) {
            $groupRoles[$group.id] = @(@(Get-Admin "groups/$($group.id)/role-mappings/realm/composite") | ForEach-Object { $_.name })
        }
        foreach ($name in $groupRoles[$group.id]) { [void]$names.Add($name) }
    }
    $held = @($audited | Where-Object { $names.Contains($_) })
    if ($held.Count -gt 0) { $realmRoles[$u.id] = $held }
}
# Users deleted during the run are left out, as if the listing had come a moment later.
$users = @($users | Where-Object { -not $deleted.Contains($_.id) })

$values = foreach ($u in $users) {
    $attributes = Get-Prop $u 'attributes'
    $tenantId = if ($attributes) { @(Get-Prop $attributes 'tenant_id') | Select-Object -First 1 } else { $null }
    $roles = ($realmRoles[$u.id] ?? @()) -join ','
    "($(Sql $u.id), $(Sql $u.username), $(Sql (Get-Prop $u 'email')), $(Sql $tenantId), " +
    "to_timestamp($([long](Get-Prop $u 'createdTimestamp') / 1000.0)), $(([bool]$u.enabled).ToString().ToLower()), $(Sql $roles))"
}

# With no users, a bare "INSERT ... VALUES ;" would abort the transaction and every query after it.
$insert = if ($values) { "INSERT INTO kc_users VALUES`n$($values -join ",`n");" } else { "-- No Keycloak users to insert." }

$sql = @"
-- Generated by scripts/audit-keycloak-users.ps1 on $(Get-Date -Format s). Read-only: rolled back at the end.
\pset pager off
BEGIN;

CREATE TEMP TABLE kc_users (
    id text PRIMARY KEY,
    username text,
    email text,
    tenant_id text,
    created timestamptz,
    enabled boolean,
    realm_roles text
);

$insert

CREATE TEMP VIEW school_rows AS
    SELECT "KeycloakSubject" AS subject, "TenantId" AS tenant_id, 'staff' AS kind FROM "Staff" WHERE "KeycloakSubject" IS NOT NULL
    UNION ALL
    SELECT "KeycloakSubject", "TenantId", 'parent' FROM "Parents" WHERE "KeycloakSubject" IS NOT NULL
    UNION ALL
    SELECT "KeycloakSubject", "TenantId", 'board' FROM "BoardMembers" WHERE "KeycloakSubject" IS NOT NULL;

\echo
\echo '1. Logins with no Staff, Parent or BoardMember row (expect only yourself as superadmin)'
SELECT k.email, k.created, k.enabled, k.realm_roles, s."Name" AS tenant_school, k.id
FROM kc_users k
LEFT JOIN "Schools" s ON s."Id"::text = lower(k.tenant_id)
WHERE NOT EXISTS (SELECT 1 FROM school_rows r WHERE r.subject = k.id)
ORDER BY k.created DESC;

\echo '2. Logins whose tenant_id differs from the school of their rows (expect none)'
SELECT k.email, r.kind, ks."Name" AS keycloak_school, rs."Name" AS row_school, k.id
FROM kc_users k
JOIN school_rows r ON r.subject = k.id
LEFT JOIN "Schools" ks ON ks."Id"::text = lower(k.tenant_id)
LEFT JOIN "Schools" rs ON rs."Id" = r.tenant_id
WHERE lower(k.tenant_id) IS DISTINCT FROM r.tenant_id::text
ORDER BY k.email;

\echo '3. Logins whose tenant_id names no school (expect none)'
SELECT k.email, k.tenant_id, k.created, k.id
FROM kc_users k
WHERE k.tenant_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM "Schools" s WHERE s."Id"::text = lower(k.tenant_id))
ORDER BY k.created DESC;

\echo '4. Logins with the Keycloak admin or superadmin role (admin still grants school admin in Phase A)'
SELECT k.email, k.realm_roles, s."Name" AS school,
       EXISTS (SELECT 1 FROM "Staff" st WHERE st."KeycloakSubject" = k.id AND st."IsAdmin") AS is_admin_in_db,
       k.id
FROM kc_users k
LEFT JOIN "Schools" s ON s."Id"::text = lower(k.tenant_id)
WHERE k.realm_roles IS NOT NULL
ORDER BY k.realm_roles, k.email;

\echo '5. Rows whose login is gone from Keycloak (housekeeping, not a security issue)'
SELECT r.kind, s."Name" AS school, r.subject
FROM school_rows r
LEFT JOIN "Schools" s ON s."Id" = r.tenant_id
WHERE NOT EXISTS (SELECT 1 FROM kc_users k WHERE k.id = r.subject)
ORDER BY s."Name", r.kind;

ROLLBACK;
"@

Set-Content -Path $OutFile -Value $sql -Encoding utf8NoBOM
Write-Host "Read $($users.Count) Keycloak users ($($realmRoles.Count) with admin or superadmin)."
Write-Host "SQL written to $OutFile. It holds names and emails: delete it when you are done."
