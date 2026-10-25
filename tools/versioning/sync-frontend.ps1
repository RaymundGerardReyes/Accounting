<#
.SYNOPSIS
    Synchronizes Frontend commits between the .NET line and the Python line.

.DESCRIPTION
    Identifies commits touching `Frontend/` present in SourceBranch but missing from TargetBranch.
    Verifies that all candidate commits strictly adhere to single-scope isolation (touching Frontend/ ONLY).
    
    Defaults to DRY-RUN mode.
    When invoked with `-Apply`, creates a branch `sync/frontend-<timestamp>` and cherry-picks
    the commits with `-x` (provenance tracking).

.PARAMETER SourceBranch
    The branch containing frontend commits to synchronize. Default: 'migrating-C#'.

.PARAMETER TargetBranch
    The branch to synchronize into. Default: 'legacy-python'.

.PARAMETER Apply
    Executes branch creation and cherry-picking. Without this flag, only a dry-run analysis is performed.

.EXAMPLE
    ./tools/versioning/sync-frontend.ps1
    ./tools/versioning/sync-frontend.ps1 -SourceBranch migrating-C# -TargetBranch legacy-python -Apply
#>

[CmdletBinding()]
param(
    [string]$SourceBranch = 'migrating-C#',
    [string]$TargetBranch = 'legacy-python',
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path

Write-Host "==============================================================" -ForegroundColor Cyan
Write-Host " R.G.E Accounting Platform - Frontend Synchronizer" -ForegroundColor Cyan
Write-Host " Source Line: $SourceBranch" -ForegroundColor Gray
Write-Host " Target Line: $TargetBranch" -ForegroundColor Gray
Write-Host " Mode:        $(if ($Apply) { 'APPLY (Active)' } else { 'DRY-RUN (Safe)' })" -ForegroundColor $(if ($Apply) { 'Yellow' } else { 'Green' })
Write-Host "==============================================================" -ForegroundColor Cyan

# 1. Identify Candidate Commits
try {
    # Reverse to get chronological order (oldest to newest)
    $logOutput = & git log --reverse --format="%H %s" "$TargetBranch..$SourceBranch" -- "Frontend/" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to query commit history between '$TargetBranch' and '$SourceBranch': $logOutput"
        exit 2
    }
} catch {
    Write-Error "Git log query failure: $_"
    exit 2
}

$commitLines = @($logOutput) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

if ($commitLines.Count -eq 0) {
    Write-Host "[OK] Target branch '$TargetBranch' is already completely up-to-date with '$SourceBranch' regarding Frontend/." -ForegroundColor Green
    exit 0
}

Write-Host "`n[INFO] Found $($commitLines.Count) candidate commit(s) touching Frontend/ in '$SourceBranch' not in '$TargetBranch':" -ForegroundColor Cyan

# 2. Audit Scope of Each Candidate Commit
$validCommits = [System.Collections.Generic.List[psobject]]::new()
$hasMixedCommits = $false

foreach ($line in $commitLines) {
    $parts = $line.Split(' ', 2)
    $sha = $parts[0]
    $subject = if ($parts.Length -gt 1) { $parts[1] } else { "" }

    # Query all files touched by this commit
    $files = & git diff-tree --no-commit-id --name-only -r $sha
    $frontendFiles = @($files) | Where-Object { $_ -like "Frontend/*" }
    $nonFrontendFiles = @($files) | Where-Object { $_ -notlike "Frontend/*" }

    if ($nonFrontendFiles.Count -gt 0) {
        $hasMixedCommits = $true
        Write-Host "  [FAIL - MIXED SCOPE] ${sha}: $subject" -ForegroundColor Red
        Write-Host "    Touches $($frontendFiles.Count) frontend files and $($nonFrontendFiles.Count) non-frontend files!" -ForegroundColor Red
        foreach ($nf in $nonFrontendFiles) {
            Write-Host "      Non-frontend path: $nf" -ForegroundColor Red
        }
    } else {
        Write-Host "  [CLEAN] ${sha}: $subject ($($frontendFiles.Count) frontend files)" -ForegroundColor Green
        $validCommits.Add([PSCustomObject]@{
            Sha     = $sha
            Subject = $subject
            Files   = $frontendFiles.Count
        })
    }
}

if ($hasMixedCommits) {
    Write-Host "`n[ERROR] One or more candidate commits violate the single-scope isolation rule." -ForegroundColor Red
    Write-Host "Mixed commits cannot be cherry-picked cleanly without polluting the target line's backend." -ForegroundColor Red
    Write-Host "Remediation: Split those commits on '$SourceBranch' into separate frontend and backend commits before syncing." -ForegroundColor Red
    exit 1
}

if (-not $Apply) {
    Write-Host "`n[DRY-RUN COMPLETE] All $($validCommits.Count) candidate commit(s) have clean single-scope isolation." -ForegroundColor Green
    Write-Host "To execute this sync and generate a synchronization branch, run:" -ForegroundColor Cyan
    Write-Host "  ./tools/versioning/sync-frontend.ps1 -SourceBranch `"$SourceBranch`" -TargetBranch `"$TargetBranch`" -Apply" -ForegroundColor Yellow
    exit 0
}

# 3. Apply Mode: Verify Clean Working Tree
$status = (& git status --porcelain 2>&1)
if (-not [string]::IsNullOrWhiteSpace($status)) {
    Write-Host "`n[ABORT] Working tree has unstaged or untracked changes. Stash or commit them before syncing." -ForegroundColor Red
    exit 1
}

$timestamp = (Get-Date).ToString("yyyyMMddHHmmss")
$syncBranch = "sync/frontend-$timestamp"

Write-Host "`n[APPLY] Creating synchronization branch '$syncBranch' from '$TargetBranch'..." -ForegroundColor Cyan

& git checkout $TargetBranch
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to checkout $TargetBranch"; exit 1 }

& git checkout -b $syncBranch
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to create branch $syncBranch"; exit 1 }

# 4. Cherry-Pick Commits with -x
Write-Host "[APPLY] Cherry-picking $($validCommits.Count) commits with provenance (-x)..." -ForegroundColor Cyan

foreach ($c in $validCommits) {
    Write-Host "  Picking $($c.Sha)..." -ForegroundColor Gray
    & git cherry-pick -x $c.Sha
    if ($LASTEXITCODE -ne 0) {
        Write-Host "`n[CONFLICT] Cherry-pick conflict encountered on $($c.Sha)." -ForegroundColor Red
        Write-Host "Resolve the conflict in Frontend/ ONLY, run 'git cherry-pick --continue', and run 'check-frontend-drift.ps1'." -ForegroundColor Yellow
        exit 1
    }
}

# 5. Run Drift Check
Write-Host "`n[VERIFICATION] Verifying frontend drift post-sync..." -ForegroundColor Cyan
& powershell -File "$PSScriptRoot/check-frontend-drift.ps1" -SourceBranch $SourceBranch -TargetBranch "HEAD"

Write-Host "`n==============================================================" -ForegroundColor Green
Write-Host " [SUCCESS] Frontend synchronization branch created: $syncBranch" -ForegroundColor Green
Write-Host " Ready to push and open PR into '$TargetBranch':" -ForegroundColor Green
Write-Host "   git push -u origin $syncBranch" -ForegroundColor Cyan
Write-Host "==============================================================" -ForegroundColor Green
