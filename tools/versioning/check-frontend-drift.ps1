<#
.SYNOPSIS
    Audits frontend drift between the .NET line (migrating-C#) and Python line (legacy-python).

.DESCRIPTION
    Compares the `Frontend/` directory between two Git references and verifies that any
    divergence is explicitly approved in `.frontend-drift-allowlist`.

    Exits 0 if zero un-allowlisted drift is detected.
    Exits 1 if unapproved drift is detected.

.PARAMETER SourceBranch
    Source Git branch or reference to compare. Default: 'legacy-python'.

.PARAMETER TargetBranch
    Target Git branch or reference to compare. Default: 'HEAD'.

.PARAMETER AllowlistPath
    Path to the allowlist file. Default: '<repo-root>/.frontend-drift-allowlist'.

.EXAMPLE
    ./tools/versioning/check-frontend-drift.ps1
    ./tools/versioning/check-frontend-drift.ps1 -SourceBranch legacy-python -TargetBranch migrating-C#
#>

[CmdletBinding()]
param(
    [string]$SourceBranch = 'legacy-python',
    [string]$TargetBranch = 'HEAD',
    [string]$AllowlistPath = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path

if ([string]::IsNullOrWhiteSpace($AllowlistPath)) {
    $AllowlistPath = Join-Path $repoRoot ".frontend-drift-allowlist"
}

Write-Host "==============================================================" -ForegroundColor Cyan
Write-Host " R.G.E Accounting Platform - Frontend Drift Audit" -ForegroundColor Cyan
Write-Host " Source: $SourceBranch" -ForegroundColor Gray
Write-Host " Target: $TargetBranch" -ForegroundColor Gray
Write-Host "==============================================================" -ForegroundColor Cyan

# 1. Parse Allowlist Patterns
$allowPatterns = [System.Collections.Generic.List[string]]::new()
if (Test-Path $AllowlistPath) {
    Get-Content $AllowlistPath | ForEach-Object {
        $line = $_.Trim()
        if (-not [string]::IsNullOrWhiteSpace($line) -and -not $line.StartsWith("#")) {
            $pattern = ($line -split "#")[0].Trim()
            if (-not [string]::IsNullOrWhiteSpace($pattern)) {
                # Normalize slashes
                $pattern = $pattern.Replace('\', '/')
                $allowPatterns.Add($pattern)
            }
        }
    }
}

Write-Host "[INFO] Loaded $($allowPatterns.Count) allowlisted drift pattern(s)." -ForegroundColor Gray

# 2. Query Git Differences in Frontend/
try {
    $diffOutput = & git diff --name-only "$SourceBranch" "$TargetBranch" -- "Frontend/" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to run git diff between '$SourceBranch' and '$TargetBranch': $diffOutput"
        exit 2
    }
} catch {
    Write-Error "Git diff execution failure: $_"
    exit 2
}

$rawChanges = @($diffOutput) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

if ($rawChanges.Count -eq 0) {
    Write-Host "[PASS] Zero drift detected! Frontend/ is 100% identical between '$SourceBranch' and '$TargetBranch'." -ForegroundColor Green
    exit 0
}

# 3. Filter Differences Against Allowlist
$unauthorizedDrift = [System.Collections.Generic.List[string]]::new()
$allowedDrift = [System.Collections.Generic.List[string]]::new()

foreach ($change in $rawChanges) {
    $normalized = $change.Trim().Replace('\', '/')
    $isAllowed = $false

    foreach ($pat in $allowPatterns) {
        # Convert glob to regex
        $regex = '^' + [regex]::Escape($pat).Replace('\*', '.*') + '$'
        if ($normalized -match $regex) {
            $isAllowed = $true
            break
        }
    }

    if ($isAllowed) {
        $allowedDrift.Add($normalized)
    } else {
        $unauthorizedDrift.Add($normalized)
    }
}

# 4. Report Results
if ($allowedDrift.Count -gt 0) {
    Write-Host "`n[NOTICE] Allowlisted divergences found ($($allowedDrift.Count) files):" -ForegroundColor Yellow
    foreach ($file in $allowedDrift) {
        Write-Host "  ~ $file (allowlisted)" -ForegroundColor Yellow
    }
}

if ($unauthorizedDrift.Count -gt 0) {
    Write-Host "`n[FAIL] Unauthorized Frontend drift detected! ($($unauthorizedDrift.Count) files):" -ForegroundColor Red
    foreach ($file in $unauthorizedDrift) {
        Write-Host "  ! $file" -ForegroundColor Red
    }

    Write-Host "`nDiff Summary:" -ForegroundColor Yellow
    & git diff --stat "$SourceBranch" "$TargetBranch" -- "Frontend/"

    Write-Host "`nRemediation:" -ForegroundColor Cyan
    Write-Host "  1. Propagate changes using tools/versioning/sync-frontend.ps1"
    Write-Host "  2. Or register approved differences in .frontend-drift-allowlist with justification."
    exit 1
}

Write-Host "`n[PASS] All divergences ($($allowedDrift.Count)) are explicitly allowlisted." -ForegroundColor Green
exit 0

