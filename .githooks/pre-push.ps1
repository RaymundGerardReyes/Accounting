<#
.SYNOPSIS
    Pre-push Git hook enforcing tag push protection and evidence-based release guardrails.
#>

param(
    [string]$RemoteName,
    [string]$RemoteUrl
)

$hasTagPush = $false
$tagList = [System.Collections.Generic.List[string]]::new()

# Read ref updates from stdin
$inputLines = @($input)
foreach ($line in $inputLines) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $parts = $line.Split(" ")
    if ($parts.Length -ge 4) {
        $localRef = $parts[0]
        $remoteRef = $parts[2]
        
        if ($remoteRef.StartsWith("refs/tags/") -or $localRef.StartsWith("refs/tags/")) {
            $hasTagPush = $true
            $tagName = if ($remoteRef.StartsWith("refs/tags/")) { $remoteRef.Substring(10) } else { $localRef.Substring(10) }
            $tagList.Add($tagName)
        }
    }
}

if ($hasTagPush) {
    if ($env:ALLOW_TAG_PUSH -ne "1") {
        Write-Host "=================================================================" -ForegroundColor Red
        Write-Host " [REJECTED] AUTONOMOUS OR UNPROTECTED TAG PUSH DETECTED" -ForegroundColor Red
        Write-Host "=================================================================" -ForegroundColor Red
        Write-Host " Attempted to push tag(s): $($tagList -join ', ')" -ForegroundColor Yellow
        Write-Host ""
        Write-Host " Under repository release policy (docs/release/versioning-policy.md):"
        Write-Host " 1. Tags cannot be pushed without explicit user approval."
        Write-Host " 2. Tags are derived strictly from ledger evidence (docs/migration/route-ledger.md)."
        Write-Host " 3. Automated agents and scripts are forbidden from pushing tags."
        Write-Host ""
        Write-Host " To push an authorized tag, explicitly execute in your shell:" -ForegroundColor Cyan
        Write-Host "   Git Bash / sh:" -ForegroundColor Cyan
        Write-Host "     ALLOW_TAG_PUSH=1 git push origin <tag_name>" -ForegroundColor White
        Write-Host "   PowerShell:" -ForegroundColor Cyan
        Write-Host "     `$env:ALLOW_TAG_PUSH = '1'; git push origin <tag_name>; Remove-Item Env:ALLOW_TAG_PUSH" -ForegroundColor White
        Write-Host "=================================================================" -ForegroundColor Red
        exit 1
    }
}

exit 0

