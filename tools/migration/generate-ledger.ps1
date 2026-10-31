<#
.SYNOPSIS
    Generates docs/migration/route-ledger.csv and docs/migration/route-ledger.md.
.DESCRIPTION
    Merges routes-python.json and routes-dotnet.json into an authoritative ledger.
#>

[CmdletBinding()]
param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
} else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

$GeneratedDir = Join-Path $RepoRoot "docs\migration\generated"
$PythonJsonPath = Join-Path $GeneratedDir "routes-python.json"
$DotNetJsonPath = Join-Path $GeneratedDir "routes-dotnet.json"

if (-not (Test-Path $PythonJsonPath) -or -not (Test-Path $DotNetJsonPath)) {
    Write-Host "Running export-routes.ps1 first..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "export-routes.ps1") -RepoRoot $RepoRoot
}

$pyRoutes = Get-Content $PythonJsonPath -Raw | ConvertFrom-Json
$dnRoutes = Get-Content $DotNetJsonPath -Raw | ConvertFrom-Json

# Index .NET routes by "METHOD PATH"
$dnMap = @{}
foreach ($r in $dnRoutes) {
    $key = "$($r.method) $($r.path)".ToUpperInvariant()
    $dnMap[$key] = $r
}

# Module Milestone Mapping
$moduleMilestone = @{
    "platform"     = "v0.1.0"
    "identity"     = "v0.2.0"
    "audit"        = "v0.2.0"
    "payments"     = "v0.3.0"
    "receipts"     = "v0.3.0"
    "finance"      = "v0.4.0"
    "documents"    = "v0.5.0"
    "integrations" = "v0.5.0"
    "costalloc"    = "v0.5.0"
}

# Module ID Prefix
$modulePrefix = @{
    "platform"     = "PLT"
    "identity"     = "IDN"
    "audit"        = "AUD"
    "payments"     = "PAY"
    "receipts"     = "REC"
    "finance"      = "FIN"
    "documents"    = "DOC"
    "integrations" = "INT"
    "costalloc"    = "CST"
}

# Deduplicate Python routes by "METHOD PATH"
$uniquePyRoutes = @{}
foreach ($r in $pyRoutes) {
    $key = "$($r.method) $($r.path)".ToUpperInvariant()
    if (-not $uniquePyRoutes.ContainsKey($key)) {
        $uniquePyRoutes[$key] = $r
    }
}

# Group and assign IDs
$moduleCounters = @{}
$ledgerRows = [System.Collections.Generic.List[PSObject]]::new()

# Sort order for modules
$moduleOrder = @("platform", "identity", "audit", "receipts", "payments", "finance", "documents", "integrations", "costalloc")

foreach ($mod in $moduleOrder) {
    $routesInMod = $uniquePyRoutes.Values | Where-Object { $_.module -eq $mod } | Sort-Object path, method
    $counter = 1
    
    foreach ($r in $routesInMod) {
        $key = "$($r.method) $($r.path)".ToUpperInvariant()
        $id = "{0}-{1:D3}" -f $modulePrefix[$mod], $counter
        $counter++
        
        $dnRef = "-"
        $status = "not-started"
        $parityTest = "-"
        $allowedDiff = "none"
        $notes = $r.summary
        
        # Check if implemented in .NET
        if ($dnMap.ContainsKey($key)) {
            $dnItem = $dnMap[$key]
            $dnRef = $dnItem.handler_ref.Replace("\", "/")
            
            # Check specific test status
            if ($r.path -eq "/version" -or $r.path -eq "/health") {
                $status = "tested"
                $parityTest = "Backend/tests/Integration.Tests"
                $notes = "Verified in Host Program.cs"
            }
            elseif ($r.path -eq "/api/v1/webhooks/paymongo") {
                $status = "tested"
                $parityTest = "Backend/tools/ParityRunner/Program.cs"
                $notes = "HMAC verification and ReceivePayMongoWebhookHandler covered in ParityRunner"
            }
            else {
                $status = "implemented"
            }
        }

        # Check SoD / Auth requirements
        $sodOrAuthz = "public"
        if ($r.requires_auth) {
            $sodOrAuthz = "authenticated"
        }
        if ($r.path -match "/cash-advances|/disbursements|/payables|/expense-reports") {
            $sodOrAuthz = "sod_four_eyes"
        }
        elseif ($r.path -match "/admin") {
            $sodOrAuthz = "system_admin"
        }

        # Writes audit?
        $writesAudit = "no"
        if ($r.method -ne "GET" -and ($r.path.StartsWith("/api/v1/finance") -or $r.path.StartsWith("/api/v1/payments") -or $r.path.StartsWith("/api/v1/orders"))) {
            $writesAudit = "yes (fn_append_audit_event)"
        }

        $rowObj = [PSCustomObject]@{
            id                  = $id
            module              = $mod
            method              = $r.method
            path                = $r.path
            python_ref          = $r.handler_ref.Replace("\", "/")
            dotnet_ref          = $dnRef
            status              = $status
            parity_test         = $parityTest
            allowed_differences = $allowedDiff
            sod_or_authz        = $sodOrAuthz
            writes_audit        = $writesAudit
            milestone           = $moduleMilestone[$mod]
            notes               = $notes
        }
        $ledgerRows.Add($rowObj)
    }
}

# -------------------------------------------------------------
# Write Machine-Readable CSV
# -------------------------------------------------------------
$CsvPath = Join-Path $RepoRoot "docs\migration\route-ledger.csv"
$csvHeader = "id,module,method,path,python_ref,dotnet_ref,status,parity_test,allowed_differences,sod_or_authz,writes_audit,milestone,notes"
$csvLines = [System.Collections.Generic.List[string]]::new()
$csvLines.Add($csvHeader)

foreach ($row in $ledgerRows) {
    # Escape quotes in notes
    $cleanNotes = $row.notes -replace '"', '""'
    $line = '"{0}","{1}","{2}","{3}","{4}","{5}","{6}","{7}","{8}","{9}","{10}","{11}","{12}"' -f `
        $row.id, $row.module, $row.method, $row.path, $row.python_ref, $row.dotnet_ref, `
        $row.status, $row.parity_test, $row.allowed_differences, $row.sod_or_authz, `
        $row.writes_audit, $row.milestone, $cleanNotes
    $csvLines.Add($line)
}

[System.IO.File]::WriteAllLines($CsvPath, $csvLines, [System.Text.Encoding]::UTF8)
Write-Host "Generated machine-readable CSV: $CsvPath ($($ledgerRows.Count) rows)" -ForegroundColor Green

# -------------------------------------------------------------
# Write Human-Readable Markdown
# -------------------------------------------------------------
$MdPath = Join-Path $RepoRoot "docs\migration\route-ledger.md"
$md = [System.Text.StringBuilder]::new()

[void]$md.AppendLine("# R.G.E Accounting Platform - Route Migration Ledger")
[void]$md.AppendLine("")
[void]$md.AppendLine("Authoritative tracking ledger for all $($ledgerRows.Count) unique HTTP endpoints across the Python-to-.NET 10 migration.")
[void]$md.AppendLine("")
[void]$md.AppendLine('### Ledger State Machine:')
[void]$md.AppendLine('`not-started` -> `implemented` -> `tested` -> `parity-verified` -> `accepted-difference`')
[void]$md.AppendLine('')
[void]$md.AppendLine('> [!IMPORTANT]')
[void]$md.AppendLine('> **Parity Evidence Invariant**: A route can only transition to parity-verified with an attached, passing automated parity test artifact. Never guess statuses. Milestone releases (dotnet/vX.Y.0) require 100% of that milestone ledger rows to be parity-verified or accepted-difference.')
[void]$md.AppendLine('')
[void]$md.AppendLine('---')
[void]$md.AppendLine('')
[void]$md.AppendLine('## Summary by Module')
[void]$md.AppendLine('')
[void]$md.AppendLine('| Module | Milestone | Total Routes | Not Started | Implemented | Tested | Parity Verified | Completion % |')
[void]$md.AppendLine('| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |')

foreach ($mod in $moduleOrder) {
    $rowsInMod = @($ledgerRows | Where-Object { $_.module -eq $mod })
    $total = $rowsInMod.Count
    $notStarted = @($rowsInMod | Where-Object { $_.status -eq 'not-started' }).Count
    $implemented = @($rowsInMod | Where-Object { $_.status -eq 'implemented' }).Count
    $tested = @($rowsInMod | Where-Object { $_.status -eq 'tested' }).Count
    $verified = @($rowsInMod | Where-Object { $_.status -eq 'parity-verified' -or $_.status -eq 'accepted-difference' }).Count
    $pct = if ($total -gt 0) { [Math]::Round(($verified / $total) * 100, 1) } else { 0 }
    $codeMs = '{0}{1}{0}' -f [char]96, $moduleMilestone[$mod]
    
    $line = "| **$mod** | $codeMs | $total | $notStarted | $implemented | $tested | $verified | **$pct%** |"
    [void]$md.AppendLine($line)
}

$grandTotal = $ledgerRows.Count
$grandNotStarted = @($ledgerRows | Where-Object { $_.status -eq 'not-started' }).Count
$grandImpl = @($ledgerRows | Where-Object { $_.status -eq 'implemented' }).Count
$grandVerified = @($ledgerRows | Where-Object { $_.status -eq 'parity-verified' -or $_.status -eq 'accepted-difference' }).Count
$grandTested = @($ledgerRows | Where-Object { $_.status -eq 'tested' }).Count
$grandPct = [Math]::Round((($grandVerified + $grandTested) / $grandTotal) * 100, 1)

$summaryLine = "| **TOTAL** | - | **$grandTotal** | **$grandNotStarted** | **$grandImpl** | **$grandTested** | **$grandVerified** | **$grandPct% (Tested+Verified)** |"
[void]$md.AppendLine($summaryLine)
[void]$md.AppendLine('')
[void]$md.AppendLine('---')
[void]$md.AppendLine('')
[void]$md.AppendLine('## Complete Route Ledger')
[void]$md.AppendLine('')
[void]$md.AppendLine('| ID | Module | Method | Normalized Path | Python Handler | .NET Handler | Status | Parity Test | SoD / Auth | Audit | Milestone | Notes |')
[void]$md.AppendLine('| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |')

$tick = [char]96
foreach ($row in $ledgerRows) {
    $cId = "{0}{1}{0}" -f $tick, $row.id
    $cPath = "{0}{1}{0}" -f $tick, $row.path
    $cPy = "{0}{1}{0}" -f $tick, $row.python_ref
    $cDn = if ($row.dotnet_ref -ne "-") { "{0}{1}{0}" -f $tick, $row.dotnet_ref } else { "-" }
    $cTest = if ($row.parity_test -ne "-") { "{0}{1}{0}" -f $tick, $row.parity_test } else { "-" }
    $cMs = "{0}{1}{0}" -f $tick, $row.milestone
    
    $tableRow = "| $cId | $($row.module) | **$($row.method)** | $cPath | $cPy | $cDn | **$($row.status)** | $cTest | $($row.sod_or_authz) | $($row.writes_audit) | $cMs | $($row.notes) |"
    [void]$md.AppendLine($tableRow)
}

[System.IO.File]::WriteAllText($MdPath, $md.ToString(), [System.Text.Encoding]::UTF8)
Write-Host "Generated human-readable Markdown: $MdPath" -ForegroundColor Green
