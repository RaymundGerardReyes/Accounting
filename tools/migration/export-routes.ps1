<#
.SYNOPSIS
    Exports normalized HTTP route catalogs from both Python FastAPI and C# .NET 10.
.DESCRIPTION
    Scans Backend/ (*.py) and Backend.DotNet/ (*.cs) for route definitions, normalizes
    path parameters to standard {param} format, maps module ownership, and outputs
    machine-readable JSON catalogs to docs/migration/generated/.
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
if (-not (Test-Path $GeneratedDir)) {
    New-Item -ItemType Directory -Force -Path $GeneratedDir | Out-Null
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " R.G.E Accounting Platform - Route Catalog Exporter v1.0" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Repository Root: $RepoRoot"

# -------------------------------------------------------------
# 1. Helper Functions
# -------------------------------------------------------------
function Normalize-RoutePath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return "/" }
    
    $clean = $Path.Trim()
    # Normalize :param to {param}
    $clean = [regex]::Replace($clean, ":([a-zA-Z0-9_]+)", '{$1}')
    # Normalize {param:guid} or {param:int} to {param}
    $clean = [regex]::Replace($clean, "\{([a-zA-Z0-9_]+):[a-zA-Z0-9_]+\}", '{$1}')
    # Ensure leading slash
    if (-not $clean.StartsWith("/")) { $clean = "/" + $clean }
    # Remove trailing slash if length > 1
    if ($clean.Length -gt 1 -and $clean.EndsWith("/")) { $clean = $clean.Substring(0, $clean.Length - 1) }
    
    return $clean
}

function Resolve-Module {
    param([string]$Path, [string]$File)
    $lowerPath = $Path.ToLowerInvariant()
    
    if ($lowerPath -match "^/version" -or $lowerPath -match "^/api/version" -or $lowerPath -match "^/health" -or $lowerPath -match "^/api/health") {
        return "platform"
    }
    if ($lowerPath.StartsWith("/api/auth") -or $lowerPath.StartsWith("/api/v1/finance/auth") -or $lowerPath -eq "/api/v1/finance/users") {
        return "identity"
    }
    if ($lowerPath -eq "/api/v1/finance/audit-events") {
        return "audit"
    }
    if ($lowerPath -match "/receipt") {
        return "receipts"
    }
    if ($lowerPath.StartsWith("/api/v1/finance/documents")) {
        return "documents"
    }
    if ($lowerPath.StartsWith("/api/v1/finance")) {
        return "finance"
    }
    if ($lowerPath.StartsWith("/api/v1/integrations")) {
        return "integrations"
    }
    if ($lowerPath.StartsWith("/api/cost-allocation") -or $lowerPath.StartsWith("/api/department") -or $lowerPath -match "^/api/(dashboard|products|variance|configuration)") {
        return "costalloc"
    }
    if ($lowerPath.StartsWith("/api/v1/orders") -or $lowerPath.StartsWith("/api/v1/payments") -or $lowerPath.StartsWith("/api/v1/webhooks") -or $lowerPath.StartsWith("/api/v1/admin") -or $lowerPath.StartsWith("/api/v1/sandbox")) {
        return "payments"
    }
    
    return "platform"
}

# -------------------------------------------------------------
# 2. Extract Python Routes (FastAPI)
# -------------------------------------------------------------
Write-Host "`n[1/3] Scanning Python FastAPI routes in Backend/..." -ForegroundColor Yellow

$PythonFiles = @(
    @{ Path = "Backend\app.py"; Prefix = "" },
    @{ Path = "Backend\finance_routes.py"; Prefix = "/api/v1/finance" },
    @{ Path = "Backend\integration_routes.py"; Prefix = "/api/v1/integrations" },
    @{ Path = "Backend\auth_routes.py"; Prefix = "/api/auth" },
    @{ Path = "Backend\cost_allocation_routes.py"; Prefix = "/api/cost-allocation" },
    @{ Path = "Backend\google_forms_routes.py"; Prefix = "/api/v1/integrations/google-forms" },
    @{ Path = "Backend\webhook_orchestration.py"; Prefix = "" }
)

$PythonRoutes = [System.Collections.Generic.List[PSObject]]::new()

foreach ($spec in $PythonFiles) {
    $fullPath = Join-Path $RepoRoot $spec.Path
    if (-not (Test-Path $fullPath)) {
        Write-Warning "File not found: $($spec.Path)"
        continue
    }

    $lines = [System.IO.File]::ReadAllLines($fullPath)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        # Match @app.get( or @router.post( or multi-line decorators
        if ($line -match "@(app|router)\.(get|post|put|patch|delete)\s*\(") {
            $method = $Matches[2].ToUpperInvariant()
            $lineNum = $i + 1
            
            # Combine up to 6 lines to parse arguments
            $decoratorSnippet = ""
            for ($k = $i; $k -lt [Math]::Min($lines.Length, $i + 8); $k++) {
                $decoratorSnippet += " " + $lines[$k]
                if ($lines[$k].Contains(")")) { break }
            }

            # Extract path string
            if ($decoratorSnippet -match '@(?:app|router)\.(?:get|post|put|patch|delete)\s*\(\s*["'']([^"'']*)["'']') {
                $rawPath = $Matches[1]
                
                # Prepend prefix if relative
                $resolvedPath = $rawPath
                if ($spec.Prefix -ne "" -and -not $rawPath.StartsWith("/api/")) {
                    $resolvedPath = $spec.Prefix
                    if ($rawPath -ne "" -and $rawPath -ne "/") {
                        if (-not $rawPath.StartsWith("/")) { $resolvedPath += "/" }
                        $resolvedPath += $rawPath
                    }
                }
                
                $normalizedPath = Normalize-RoutePath $resolvedPath
                
                # Extract summary
                $summary = ""
                if ($decoratorSnippet -match 'summary\s*=\s*["'']([^"'']+)["'']') {
                    $summary = $Matches[1]
                }
                
                # Check auth requirement in handler signature or decorator
                $requiresAuth = $false
                for ($j = $i; $j -lt [Math]::Min($lines.Length, $i + 25); $j++) {
                    if ($lines[$j] -match "Depends\(|current_user|verify_role|verify_csrf|Security\(") {
                        $requiresAuth = $true
                        break
                    }
                    if ($lines[$j] -match "^def\s+|^async\s+def\s+") {
                        # scan through the def signature
                        continue
                    }
                }

                $module = Resolve-Module -Path $normalizedPath -File $spec.Path

                $routeObj = [PSCustomObject]@{
                    method        = $method
                    path          = $normalizedPath
                    module        = $module
                    handler_ref   = "$($spec.Path):$lineNum"
                    summary       = $summary
                    requires_auth = $requiresAuth
                    runtime       = "python"
                }
                $PythonRoutes.Add($routeObj)
            }
        }
    }
}

Write-Host "Extracted $($PythonRoutes.Count) Python route definitions." -ForegroundColor Green

# -------------------------------------------------------------
# 3. Extract .NET Routes (Minimal API)
# -------------------------------------------------------------
Write-Host "`n[2/3] Scanning C# .NET 10 Minimal API routes in Backend.DotNet/..." -ForegroundColor Yellow

$DotNetRoutes = [System.Collections.Generic.List[PSObject]]::new()
$DotNetFiles = Get-ChildItem -Path (Join-Path $RepoRoot "Backend.DotNet") -Recurse -Filter "*.cs"

foreach ($file in $DotNetFiles) {
    $relPath = $file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        # Match app.MapGet("/path", ...), group.MapPost("/path", ...)
        if ($line -match '\.(MapGet|MapPost|MapPut|MapPatch|MapDelete)\s*\(\s*["'']([^"'']+)["'']') {
            $method = $Matches[1].Replace("Map", "").ToUpperInvariant()
            $rawPath = $Matches[2]
            $lineNum = $i + 1
            $normalizedPath = Normalize-RoutePath $rawPath
            $module = Resolve-Module -Path $normalizedPath -File $relPath
            
            $routeObj = [PSCustomObject]@{
                method        = $method
                path          = $normalizedPath
                module        = $module
                handler_ref   = "$relPath`:$lineNum"
                summary       = ""
                requires_auth = ($line -match "RequireAuthorization")
                runtime       = "dotnet"
            }
            $DotNetRoutes.Add($routeObj)
        }
    }
}

Write-Host "Extracted $($DotNetRoutes.Count) .NET route definitions." -ForegroundColor Green

# -------------------------------------------------------------
# 4. Save Catalogs & Calculate Parity / Unique Stats
# -------------------------------------------------------------
Write-Host "`n[3/3] Saving generated route catalogs..." -ForegroundColor Yellow

$PythonJsonPath = Join-Path $GeneratedDir "routes-python.json"
$DotNetJsonPath = Join-Path $GeneratedDir "routes-dotnet.json"

$PythonRoutes | ConvertTo-Json -Depth 5 | Set-Content -Path $PythonJsonPath -Encoding utf8
$DotNetRoutes | ConvertTo-Json -Depth 5 | Set-Content -Path $DotNetJsonPath -Encoding utf8

Write-Host "Saved: $PythonJsonPath" -ForegroundColor Cyan
Write-Host "Saved: $DotNetJsonPath" -ForegroundColor Cyan

# Compute unique keys "METHOD PATH"
$pyKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($r in $PythonRoutes) {
    [void]$pyKeys.Add("$($r.method) $($r.path)")
}

$dnKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($r in $DotNetRoutes) {
    [void]$dnKeys.Add("$($r.method) $($r.path)")
}

$allUniqueKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($k in $pyKeys) { [void]$allUniqueKeys.Add($k) }
foreach ($k in $dnKeys) { [void]$allUniqueKeys.Add($k) }

$onlyPy = [System.Collections.Generic.List[string]]::new()
foreach ($k in $pyKeys) {
    if (-not $dnKeys.Contains($k)) { $onlyPy.Add($k) }
}

$onlyDn = [System.Collections.Generic.List[string]]::new()
foreach ($k in $dnKeys) {
    if (-not $pyKeys.Contains($k)) { $onlyDn.Add($k) }
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " ROUTE ENUMERATION SUMMARY" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Total Python Route Handlers : $($PythonRoutes.Count)"
Write-Host " Unique Python Endpoints     : $($pyKeys.Count)"
Write-Host " Total .NET Route Handlers   : $($DotNetRoutes.Count)"
Write-Host " Unique .NET Endpoints       : $($dnKeys.Count)"
Write-Host " Combined Unique Endpoints   : $($allUniqueKeys.Count)"
Write-Host " Routes Only in Python (Gap) : $($onlyPy.Count)" -ForegroundColor Yellow
Write-Host " Routes Only in .NET         : $($onlyDn.Count)" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
