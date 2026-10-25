<#
.SYNOPSIS
    Scans SQL migrations for destructive DDL operations that break dual-line compatibility.

.DESCRIPTION
    Audits new or modified SQL scripts for:
      - DROP COLUMN
      - DROP TABLE
      - RENAME COLUMN / RENAME TABLE
      - Non-nullable columns added without DEFAULT
      - DROP FUNCTION on shared stored procedures

    Exits 0 if clean, exits 1 if destructive statements are detected.

.PARAMETER TargetPath
    Path to directory or file to inspect. Default: 'Backend.DotNet/db/migrations'.

.EXAMPLE
    ./tools/versioning/check-migration-compat.ps1
    ./tools/versioning/check-migration-compat.ps1 -TargetPath "Backend.DotNet/db/migrations"
#>

[CmdletBinding()]
param(
    [string]$TargetPath = "Backend.DotNet/db/migrations"
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$scanPath = Join-Path $repoRoot $TargetPath

Write-Host "==============================================================" -ForegroundColor Cyan
Write-Host " R.G.E Accounting Platform - Migration Compatibility Gate" -ForegroundColor Cyan
Write-Host " Target Path: $TargetPath" -ForegroundColor Gray
Write-Host "==============================================================" -ForegroundColor Cyan

if (-not (Test-Path $scanPath)) {
    Write-Host "[NOTICE] Scan path '$TargetPath' does not exist yet. Skipping." -ForegroundColor Yellow
    exit 0
}

$sqlFiles = Get-ChildItem -Path $scanPath -Filter "*.sql" -Recurse

if ($sqlFiles.Count -eq 0) {
    Write-Host "[PASS] No SQL migration files found to inspect." -ForegroundColor Green
    exit 0
}

Write-Host "[INFO] Inspecting $($sqlFiles.Count) SQL file(s)..." -ForegroundColor Gray

$destructivePatterns = @(
    @{ Name = "DROP COLUMN"; Regex = '(?i)\bDROP\s+COLUMN\b' },
    @{ Name = "DROP TABLE"; Regex = '(?i)\bDROP\s+TABLE\b' },
    @{ Name = "RENAME COLUMN"; Regex = '(?i)\bRENAME\s+COLUMN\b' },
    @{ Name = "RENAME TABLE"; Regex = '(?i)\bRENAME\s+TABLE\b' },
    @{ Name = "DROP CONSTRAINT"; Regex = '(?i)\bDROP\s+CONSTRAINT\b' },
    @{ Name = "DROP FUNCTION"; Regex = '(?i)\bDROP\s+FUNCTION\b' }
)

$violations = [System.Collections.Generic.List[psobject]]::new()

foreach ($file in $sqlFiles) {
    $content = Get-Content $file.FullName -Raw
    $lines = Get-Content $file.FullName

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $lineText = $lines[$i].Trim()

        # Skip comment lines
        if ($lineText.StartsWith("--") -or [string]::IsNullOrWhiteSpace($lineText)) {
            continue
        }

        foreach ($p in $destructivePatterns) {
            if ($lineText -match $p.Regex) {
                $violations.Add([PSCustomObject]@{
                    File       = $file.Name
                    LineNumber = $i + 1
                    Rule       = $p.Name
                    Snippet    = $lineText
                })
            }
        }

        # Check ADD COLUMN NOT NULL without DEFAULT
        if ($lineText -match '(?i)\bADD\s+COLUMN\b' -and $lineText -match '(?i)\bNOT\s+NULL\b' -and $lineText -notmatch '(?i)\bDEFAULT\b') {
            $violations.Add([PSCustomObject]@{
                File       = $file.Name
                LineNumber = $i + 1
                Rule       = "ADD COLUMN NOT NULL without DEFAULT"
                Snippet    = $lineText
            })
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "`n[FATAL] Found $($violations.Count) destructive schema violation(s) violating Dual-Line Rules:" -ForegroundColor Red
    foreach ($v in $violations) {
        Write-Host "  ! $($v.File):L$($v.LineNumber) [$($v.Rule)]" -ForegroundColor Red
        Write-Host "      $($v.Snippet)" -ForegroundColor Yellow
    }
    Write-Host "`nRefer to docs/release/shared-database-rules.md for safe two-phase expansion procedures." -ForegroundColor Cyan
    exit 1
}

Write-Host "`n[PASS] All $($sqlFiles.Count) migration file(s) adhere strictly to additive-only invariants." -ForegroundColor Green
exit 0

