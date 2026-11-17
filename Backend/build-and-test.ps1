param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [int]$MaxCpuCount = 1,

    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$env:MSBUILDDISABLENODEREUSE = "1"
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_XMLDOC_MODE = "skip"

dotnet build-server shutdown | Out-Null

dotnet restore .\Rge.Accounting.slnx `
    --disable-parallel `
    -m:$MaxCpuCount `
    /nr:false `
    /p:NuGetInteractive=false `
    /p:DisableImplicitNuGetFallbackFolder=true
if ($LASTEXITCODE -ne 0) { throw "NuGet restore failed with exit code $LASTEXITCODE" }

dotnet build .\Rge.Accounting.slnx `
    --no-restore `
    --configuration $Configuration `
    --disable-build-servers `
    -m:$MaxCpuCount `
    /nr:false
if ($LASTEXITCODE -ne 0) { throw "Solution build failed with exit code $LASTEXITCODE" }

dotnet run --project .\tools\ParityRunner\Rge.ParityRunner.csproj `
    --no-build `
    --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "ParityRunner verification failed with exit code $LASTEXITCODE" }

if (-not $SkipTests) {
    dotnet test .\Rge.Accounting.slnx `
        --no-build `
        --configuration $Configuration `
        --disable-build-servers `
        -m:$MaxCpuCount
    if ($LASTEXITCODE -ne 0) { throw "xUnit test run failed with exit code $LASTEXITCODE" }
}
