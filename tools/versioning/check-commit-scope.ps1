<#
.SYNOPSIS
    Verifies that Git commits respect the strict Separation of Commit Scopes.
.DESCRIPTION
    Fails if:
    1. A commit touches BOTH Frontend/ and any backend folder (Backend/, db/),
       except for 'chore(release)', 'chore(deps)', 'build(release)', or 'build(deps)'.
    2. A commit with scope 'web' touches any file outside Frontend/.
.PARAMETER CommitRef
    Commit hash, branch, or revision range to check (default: HEAD).
#>
[CmdletBinding()]
param(
    [Alias('CommitRange')]
    [string]$CommitRef = "HEAD"
)

$ErrorActionPreference = "Stop"

Write-Host "================================================================"
Write-Host " Checking Commit Scope Rules for: $CommitRef"
Write-Host "================================================================"

# Get list of commits to evaluate
$commitList = @()
if ($CommitRef.Contains("..")) {
    $commitList = git log --format="%H" $CommitRef
} else {
    $commitList = @(git rev-parse $CommitRef)
}

$hasViolations = $false

foreach ($sha in $commitList) {
    if ([string]::IsNullOrWhiteSpace($sha)) { continue }

    $msg = git log -n 1 --format="%s" $sha
    $files = git diff-tree --no-commit-id --name-only -r $sha

    # Parse conventional commit type and scope
    $type = ""
    $scope = ""
    if ($msg -match '^(?<type>[a-z]+)(\((?<scope>[a-z0-9_-]+)\))?!?:\s*(?<desc>.+)$') {
        $type = $Matches['type']
        $scope = $Matches['scope']
    }

    $isExempt = ($type -in @("chore", "build") -and $scope -in @("release", "deps"))

    $touchesFrontend = $false
    $touchesBackend = $false
    $outsideFrontend = @()

    foreach ($f in $files) {
        if ($f -like "Frontend/*") {
            $touchesFrontend = $true
        } else {
            $outsideFrontend += $f
        }

        if ($f -like "Backend/*" -or $f -like "db/*") {
            $touchesBackend = $true
        }
    }

    # Rule 1: No mixed frontend and backend commits
    if ($touchesFrontend -and $touchesBackend -and -not $isExempt) {
        Write-Host " [VIOLATION] Commit $sha touches both Frontend and Backend paths:" -ForegroundColor Red
        Write-Host "   Subject: $msg" -ForegroundColor Yellow
        Write-Host "   Files: $($files -join ', ')" -ForegroundColor DarkGray
        $hasViolations = $true
    }

    # Rule 2: Scope 'web' must only touch Frontend/
    if ($scope -eq "web" -and $outsideFrontend.Count -gt 0) {
        Write-Host " [VIOLATION] Commit $sha with scope 'web' touches files outside Frontend/:" -ForegroundColor Red
        Write-Host "   Subject: $msg" -ForegroundColor Yellow
        Write-Host "   Non-Frontend Files: $($outsideFrontend -join ', ')" -ForegroundColor DarkGray
        $hasViolations = $true
    }
}

if ($hasViolations) {
    Write-Host "`nCommit scope check FAILED." -ForegroundColor Red
    exit 1
} else {
    Write-Host "`nAll checked commits satisfy scope isolation rules." -ForegroundColor Green
    exit 0
}
