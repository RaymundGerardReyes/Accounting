<#
.SYNOPSIS
    Audits docs/migration/route-ledger.csv for integrity and outputs milestone readiness.
.DESCRIPTION
    Validates that:
    1. Every 'parity-verified' row has a real, existing parity test evidence artifact.
    2. Every 'dotnet_ref' handler points to an existing C# file on disk.
    3. Outputs module completion percentages and milestone release readiness.
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

$CsvPath = Join-Path $RepoRoot "docs\migration\route-ledger.csv"
if (-not (Test-Path $CsvPath)) {
    Write-Error "Route ledger CSV not found at $CsvPath. Run tools/migration/generate-ledger.ps1 first."
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " R.G.E Accounting Platform - Migration Ledger Auditor" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Ledger Source: $CsvPath"

$rows = Import-Csv -Path $CsvPath -Encoding utf8
Write-Host "Loaded $($rows.Count) ledger rows.`n"

# -------------------------------------------------------------
# 1. Invariant Validation
# -------------------------------------------------------------
$violations = [System.Collections.Generic.List[string]]::new()

foreach ($row in $rows) {
    # Check 1: Parity-verified rows MUST have a real, existing test file/artifact
    if ($row.status -eq "parity-verified") {
        if ([string]::IsNullOrWhiteSpace($row.parity_test) -or $row.parity_test -eq "-") {
            $violations.Add("Row $($row.id) is marked 'parity-verified' but has no parity_test evidence.")
        } else {
            $testPath = Join-Path $RepoRoot $row.parity_test.Replace("/", "\")
            if (-not (Test-Path $testPath)) {
                $violations.Add("Row $($row.id) links to non-existent parity_test artifact: $($row.parity_test)")
            }
        }
    }

    # Check 2: DotNet handler ref must point to an existing file if not '-'
    if (-not [string]::IsNullOrWhiteSpace($row.dotnet_ref) -and $row.dotnet_ref -ne "-") {
        $filePart = $row.dotnet_ref.Split(":")[0].Replace("/", "\")
        $handlerPath = Join-Path $RepoRoot $filePart
        if (-not (Test-Path $handlerPath)) {
            $violations.Add("Row $($row.id) references non-existent .NET handler file: $filePart")
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "LEDGER INVARIANT VIOLATIONS DETECTED ($($violations.Count)):" -ForegroundColor Red
    foreach ($v in $violations) {
        Write-Host "  - $v" -ForegroundColor Red
    }
    exit 1
}

Write-Host "[OK] All ledger invariants verified cleanly. 0 violations." -ForegroundColor Green

# -------------------------------------------------------------
# 2. Per-Module Completion Table
# -------------------------------------------------------------
Write-Host "`nMODULE COMPLETION SUMMARY" -ForegroundColor Cyan
Write-Host "--------------------------------------------------------------------------------------------------"
Write-Host ("{0,-15} {1,-10} {2,6} {3,12} {4,12} {5,8} {6,15} {7,14}" -f "Module", "Milestone", "Total", "NotStarted", "Implemented", "Tested", "ParityVerified", "Completion %")
Write-Host "--------------------------------------------------------------------------------------------------"

$modules = $rows | Select-Object -ExpandProperty module -Unique
$moduleMilestones = @{}

foreach ($mod in $modules) {
    $modRows = @($rows | Where-Object { $_.module -eq $mod })
    $total = $modRows.Count
    $ms = ($modRows[0].milestone)
    $moduleMilestones[$mod] = $ms
    $notStarted = @($modRows | Where-Object { $_.status -eq "not-started" }).Count
    $implemented = @($modRows | Where-Object { $_.status -eq "implemented" }).Count
    $tested = @($modRows | Where-Object { $_.status -eq "tested" }).Count
    $verified = @($modRows | Where-Object { $_.status -eq "parity-verified" -or $_.status -eq "accepted-difference" }).Count
    $completionPct = if ($total -gt 0) { [Math]::Round(($verified / $total) * 100, 1) } else { 0 }
    
    Write-Host ("{0,-15} {1,-10} {2,6} {3,12} {4,12} {5,8} {6,15} {7,13}%" -f $mod, $ms, $total, $notStarted, $implemented, $tested, $verified, $completionPct)
}

$grandTotal = $rows.Count
$grandVerified = @($rows | Where-Object { $_.status -eq "parity-verified" -or $_.status -eq "accepted-difference" }).Count
$grandTested = @($rows | Where-Object { $_.status -eq "tested" }).Count
$grandPct = [Math]::Round(($grandVerified / $grandTotal) * 100, 1)

Write-Host "--------------------------------------------------------------------------------------------------"
Write-Host ("{0,-15} {1,-10} {2,6} {3,12} {4,12} {5,8} {6,15} {7,13}%" -f "TOTAL", "-", $grandTotal, @($rows | Where-Object { $_.status -eq "not-started" }).Count, @($rows | Where-Object { $_.status -eq "implemented" }).Count, $grandTested, $grandVerified, $grandPct)
Write-Host "--------------------------------------------------------------------------------------------------"

# -------------------------------------------------------------
# 3. Milestone Readiness Table
# -------------------------------------------------------------
Write-Host "`nMILESTONE RELEASE READINESS" -ForegroundColor Cyan
Write-Host "--------------------------------------------------------------------------------------------------"
Write-Host ("{0,-18} {1,-35} {2,6} {3,10} {4,14}" -f "Milestone", "Modules Included", "Rows", "Verified", "Ready for Tag?")
Write-Host "--------------------------------------------------------------------------------------------------"

$milestoneGroups = @(
    @{ Name = "dotnet/v0.1.0"; Modules = @("platform"); Desc = "Foundation" },
    @{ Name = "dotnet/v0.2.0"; Modules = @("identity", "audit"); Desc = "Identity & Audit" },
    @{ Name = "dotnet/v0.3.0"; Modules = @("payments", "receipts"); Desc = "Payments & Receipts" },
    @{ Name = "dotnet/v0.4.0"; Modules = @("finance"); Desc = "Finance Operations" },
    @{ Name = "dotnet/v0.5.0"; Modules = @("documents", "integrations", "costalloc"); Desc = "Integrations, CostAlloc, Docs" }
)

foreach ($mg in $milestoneGroups) {
    $mRows = @($rows | Where-Object { $mg.Modules -contains $_.module })
    $mTotal = $mRows.Count
    $mVerified = @($mRows | Where-Object { $_.status -eq "parity-verified" -or $_.status -eq "accepted-difference" }).Count
    
    $ready = if ($mTotal -gt 0 -and $mVerified -eq $mTotal) { "READY" } else { "NOT READY" }
    $color = if ($ready -eq "READY") { "Green" } else { "DarkYellow" }
    
    $modList = $mg.Modules -join ", "
    Write-Host ("{0,-18} {1,-35} {2,6} {3,10} " -f $mg.Name, "$($mg.Desc) ($modList)", $mTotal, "$mVerified/$mTotal") -NoNewline
    Write-Host ("{0,14}" -f $ready) -ForegroundColor $color
}
Write-Host "--------------------------------------------------------------------------------------------------"

Write-Host "`nAudit Check Completed Successfully." -ForegroundColor Green
exit 0

