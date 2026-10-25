# ==============================================================================
# R.G.E Accounting Platform - .NET 10 Architecture & Solution Scaffold Script
# Style: Modular Monolith + Clean Architecture + Vertical Slices (Skeleton Only)
# ==============================================================================

[CmdletBinding()]
param(
    [string]$TargetDir = "e:\Accounting\Backend.DotNet"
)

$ErrorActionPreference = "Stop"

$DotNetSdkVersion = "10.0.301"
$TargetFramework = "net10.0"
$SolutionName = "Rge.Accounting"

Write-Host "=============================================================================="
Write-Host " Scaffolding R.G.E Accounting Platform (.NET 10 Skeleton)"
Write-Host " Target Directory : $TargetDir"
Write-Host " Target Framework : $TargetFramework"
Write-Host " Pinned SDK       : $DotNetSdkVersion"
Write-Host "=============================================================================="

if (-not (Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
}

function Ensure-ParentDir([string]$Path) {
    $parent = [System.IO.Path]::GetDirectoryName($Path)
    if ($parent -and -not (Test-Path $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
}

function Write-Utf8File([string]$RelPath, [string]$Content) {
    $fullPath = [System.IO.Path]::Combine($TargetDir, $RelPath)
    Ensure-ParentDir $fullPath
    [System.IO.File]::WriteAllText($fullPath, $Content, [System.Text.Encoding]::UTF8)
}

function Touch-Files([string[]]$RelPaths) {
    foreach ($rel in $RelPaths) {
        $fullPath = [System.IO.Path]::Combine($TargetDir, $rel)
        Ensure-ParentDir $fullPath
        if (-not (Test-Path $fullPath)) {
            [System.IO.File]::WriteAllText($fullPath, "", [System.Text.Encoding]::UTF8)
        }
    }
}

function Create-DomainCsproj([string]$RelPath) {
    $content = @"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\BuildingBlocks\Rge.BuildingBlocks.SharedKernel\Rge.BuildingBlocks.SharedKernel.csproj" />
  </ItemGroup>
</Project>
"@
    Write-Utf8File $RelPath $content
}

function Create-ContractsCsproj([string]$RelPath) {
    $content = @"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\BuildingBlocks\Rge.BuildingBlocks.SharedKernel\Rge.BuildingBlocks.SharedKernel.csproj" />
    <ProjectReference Include="..\..\..\BuildingBlocks\Rge.BuildingBlocks.Application\Rge.BuildingBlocks.Application.csproj" />
  </ItemGroup>
</Project>
"@
    Write-Utf8File $RelPath $content
}

function Create-AppCsproj([string]$ModuleName) {
    $relPath = "src/Modules/$ModuleName/Rge.$ModuleName.Application/Rge.$ModuleName.Application.csproj"
    $content = @"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\BuildingBlocks\Rge.BuildingBlocks.Application\Rge.BuildingBlocks.Application.csproj" />
    <ProjectReference Include="..\Rge.$ModuleName.Domain\Rge.$ModuleName.Domain.csproj" />
    <ProjectReference Include="..\Rge.$ModuleName.Contracts\Rge.$ModuleName.Contracts.csproj" />
  </ItemGroup>
</Project>
"@
    Write-Utf8File $relPath $content
}

function Create-InfraCsproj([string]$ModuleName) {
    $relPath = "src/Modules/$ModuleName/Rge.$ModuleName.Infrastructure/Rge.$ModuleName.Infrastructure.csproj"
    $content = @"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\BuildingBlocks\Rge.BuildingBlocks.Infrastructure\Rge.BuildingBlocks.Infrastructure.csproj" />
    <ProjectReference Include="..\Rge.$ModuleName.Application\Rge.$ModuleName.Application.csproj" />
    <ProjectReference Include="..\Rge.$ModuleName.Domain\Rge.$ModuleName.Domain.csproj" />
  </ItemGroup>
</Project>
"@
    Write-Utf8File $relPath $content
}

function Create-ApiCsproj([string]$ModuleName) {
    $relPath = "src/Modules/$ModuleName/Rge.$ModuleName.Api/Rge.$ModuleName.Api.csproj"
    $content = @"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\BuildingBlocks\Rge.BuildingBlocks.Api\Rge.BuildingBlocks.Api.csproj" />
    <ProjectReference Include="..\Rge.$ModuleName.Application\Rge.$ModuleName.Application.csproj" />
    <ProjectReference Include="..\Rge.$ModuleName.Contracts\Rge.$ModuleName.Contracts.csproj" />
  </ItemGroup>
</Project>
"@
    Write-Utf8File $relPath $content
}

function Create-TestCsproj([string]$RelPath) {
    $content = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
</Project>
"@
    Write-Utf8File $RelPath $content
}

function Create-ToolCsproj([string]$RelPath) {
    $content = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
"@
    Write-Utf8File $RelPath $content
}

function Create-VerticalSlices([string]$BaseDir, [string[]]$Slices) {
    foreach ($slice in $Slices) {
        $sliceDir = "$BaseDir/$slice"
        Touch-Files @("$sliceDir/${slice}Handler.cs")
    }
}

# ==============================================================================
# STEP 1: Root Configuration & Governance Files
# ==============================================================================
Write-Host "[1/9] Creating root configuration files..."

$globalJson = @"
{
  "sdk": {
    "version": "$DotNetSdkVersion",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
"@
Write-Utf8File "global.json" $globalJson

$dirBuildProps = @"
<Project>
  <PropertyGroup>
    <TargetFramework>$TargetFramework</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-all</AnalysisLevel>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'`$(CI)' == 'true'">true</ContinuousIntegrationBuild>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <DisableImplicitNuGetFallbackFolder>true</DisableImplicitNuGetFallbackFolder>
  </PropertyGroup>

  <!-- Exclude Sandbox & Dev-Only tools from Release/Production builds -->
  <PropertyGroup Condition="'`$(Configuration)' == 'Debug'">
    <DefineConstants>`$(DefineConstants);SANDBOX_TOOLS</DefineConstants>
  </PropertyGroup>
</Project>
"@
Write-Utf8File "Directory.Build.props" $dirBuildProps

$dirPackagesProps = @"
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <!-- Data Access & Migrations -->
    <PackageVersion Include="Npgsql" Version="9.0.2" />
    <PackageVersion Include="Dapper" Version="2.1.35" />
    <PackageVersion Include="dbup-postgresql" Version="6.0.0" />
    <!-- Caching & Resilience -->
    <PackageVersion Include="Microsoft.Extensions.Caching.Hybrid" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="9.0.0" />
    <!-- Observability -->
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.10.0" />
    <PackageVersion Include="Serilog.AspNetCore" Version="9.0.0" />
    <!-- PDF & Integrations -->
    <PackageVersion Include="QuestPDF" Version="2024.12.0" />
    <!-- Testing & Architecture Verification -->
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="NetArchTest.Rules" Version="1.3.2" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.1.0" />
    <PackageVersion Include="NBomber" Version="5.8.1" />
    <PackageVersion Include="BenchmarkDotNet" Version="0.14.0" />
  </ItemGroup>
</Project>
"@
Write-Utf8File "Directory.Packages.props" $dirPackagesProps

$nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
Write-Utf8File "nuget.config" $nugetConfig

$globalConfig = @"
is_global = true

# Security rules enforced as build errors (SQL injection, weak crypto, path traversal, etc.)
dotnet_diagnostic.CA2100.severity = error
dotnet_diagnostic.CA3001.severity = error
dotnet_diagnostic.CA3002.severity = error
dotnet_diagnostic.CA3003.severity = error
dotnet_diagnostic.CA3004.severity = error
dotnet_diagnostic.CA3006.severity = error
dotnet_diagnostic.CA5350.severity = error
dotnet_diagnostic.CA5351.severity = error
"@
Write-Utf8File ".globalconfig" $globalConfig

Touch-Files @(
  ".editorconfig",
  ".gitignore",
  "README.md"
)

# ==============================================================================
# STEP 2: Documentation (docs/) & Database (db/) Trees
# ==============================================================================
Write-Host "[2/9] Creating docs/ and db/ structure..."

Touch-Files @(
  "docs/README.md",
  "docs/architecture/c4-context.md",
  "docs/architecture/c4-container.md",
  "docs/architecture/c4-component-per-module.md",
  "docs/architecture/module-dependency-map.md",
  "docs/architecture/sequence-diagrams/payment-webhook-flow.md",
  "docs/architecture/sequence-diagrams/outbox-fulfillment-flow.md",
  "docs/architecture/sequence-diagrams/cash-advance-four-eyes-flow.md",
  "docs/architecture/sequence-diagrams/audit-hash-chain-flow.md",
  "docs/adr/0001-modular-monolith.md",
  "docs/adr/0002-dapper-npgsql-over-efcore.md",
  "docs/adr/0003-keep-postgres-stored-functions.md",
  "docs/adr/0004-money-as-long-centavos.md",
  "docs/adr/0005-transactional-outbox.md",
  "docs/adr/0006-hybridcache-strategy.md",
  "docs/adr/0007-authn-authz-model.md",
  "docs/adr/0008-native-aot-vs-jit-readytorun.md",
  "docs/adr/0009-strangler-fig-migration.md",
  "docs/adr/0010-error-handling-result-pattern.md",
  "docs/security/threat-model-stride.md",
  "docs/security/owasp-asvs-checklist.md",
  "docs/security/secrets-management.md",
  "docs/security/sod-matrix.md",
  "docs/runbooks/incident-webhook-backlog.md",
  "docs/runbooks/incident-audit-chain-break.md",
  "docs/runbooks/rollback-cutover.md",
  "docs/runbooks/key-rotation.md",
  "docs/api/openapi.v1.json",
  "docs/api/endpoint-parity-matrix.md",
  "docs/performance/latency-budgets.md",
  "docs/performance/benchmark-baselines.md",

  "db/migrations/V0001__baseline_from_postgres_live_schema.sql",
  "db/migrations/V0002__indexes_hot_paths.sql",
  "db/migrations/V0003__audit_immutability_triggers.sql",
  "db/migrations/V0004__outbox_lease_columns.sql",
  "db/functions/fn_sanitize_input.sql",
  "db/functions/fn_next_sequence_number.sql",
  "db/functions/fn_append_audit_event.sql",
  "db/functions/fn_authenticate_user.sql",
  "db/functions/fn_oauth_state.sql",
  "db/seed/approval_matrix.sql",
  "db/seed/holiday_calendar_ph.sql",
  "db/seed/tax_codes.sql",
  "db/roles/app_runtime_role.sql",
  "db/roles/app_migrator_role.sql",
  "db/roles/app_readonly_role.sql"
)

# ==============================================================================
# STEP 3: Host (Composition Root)
# ==============================================================================
Write-Host "[3/9] Scaffolding src/Host/Rge.Accounting.Host..."

$hostCsproj = @"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <ProjectReference Include="..\..\BuildingBlocks\Rge.BuildingBlocks.Api\Rge.BuildingBlocks.Api.csproj" />
    <ProjectReference Include="..\..\BuildingBlocks\Rge.BuildingBlocks.Infrastructure\Rge.BuildingBlocks.Infrastructure.csproj" />
  </ItemGroup>
</Project>
"@
Write-Utf8File "src/Host/Rge.Accounting.Host/Rge.Accounting.Host.csproj" $hostCsproj

Touch-Files @(
  "src/Host/Rge.Accounting.Host/Program.cs",
  "src/Host/Rge.Accounting.Host/appsettings.json",
  "src/Host/Rge.Accounting.Host/appsettings.Development.json",
  "src/Host/Rge.Accounting.Host/appsettings.Production.json",
  "src/Host/Rge.Accounting.Host/Composition/ModuleRegistry.cs",
  "src/Host/Rge.Accounting.Host/Composition/ServiceCollectionExtensions.cs",
  "src/Host/Rge.Accounting.Host/Composition/EndpointRouteBuilderExtensions.cs",
  "src/Host/Rge.Accounting.Host/Pipeline/CorrelationIdMiddleware.cs",
  "src/Host/Rge.Accounting.Host/Pipeline/SecurityHeadersMiddleware.cs",
  "src/Host/Rge.Accounting.Host/Pipeline/GlobalExceptionHandler.cs",
  "src/Host/Rge.Accounting.Host/Pipeline/RequestSizeLimitPolicy.cs",
  "src/Host/Rge.Accounting.Host/Pipeline/IdempotencyKeyMiddleware.cs",
  "src/Host/Rge.Accounting.Host/Pipeline/RequestLoggingEnricher.cs",
  "src/Host/Rge.Accounting.Host/Configuration/KestrelConfiguration.cs",
  "src/Host/Rge.Accounting.Host/Configuration/RateLimiterConfiguration.cs",
  "src/Host/Rge.Accounting.Host/Configuration/CorsConfiguration.cs",
  "src/Host/Rge.Accounting.Host/Configuration/OpenTelemetryConfiguration.cs",
  "src/Host/Rge.Accounting.Host/Configuration/SecretsConfiguration.cs",
  "src/Host/Rge.Accounting.Host/Properties/launchSettings.json"
)

# ==============================================================================
# STEP 4: BuildingBlocks
# ==============================================================================
Write-Host "[4/9] Scaffolding src/BuildingBlocks..."

# 4.1 Rge.BuildingBlocks.SharedKernel
$sharedKernelCsproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <!-- Pure SharedKernel: Zero NuGet or external project dependencies allowed -->
</Project>
"@
Write-Utf8File "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Rge.BuildingBlocks.SharedKernel.csproj" $sharedKernelCsproj

Touch-Files @(
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Primitives/Entity.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Primitives/AggregateRoot.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Primitives/ValueObject.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Primitives/DomainEvent.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Primitives/IClock.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Money/Money.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Money/Currency.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Money/VatCalculator.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Money/Rounding.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Results/Result.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Results/Error.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Results/ErrorType.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Time/PhilippineBusinessCalendar.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Time/IBusinessDayService.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Guards/Guard.cs"
)

# 4.2 Rge.BuildingBlocks.Application
$appBbCsproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Rge.BuildingBlocks.SharedKernel\Rge.BuildingBlocks.SharedKernel.csproj" />
  </ItemGroup>
</Project>
"@
Write-Utf8File "src/BuildingBlocks/Rge.BuildingBlocks.Application/Rge.BuildingBlocks.Application.csproj" $appBbCsproj

Touch-Files @(
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Abstractions/ICommand.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Abstractions/IQuery.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Abstractions/IHandler.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Abstractions/IUnitOfWork.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Abstractions/ICurrentActor.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Behaviors/ValidationBehavior.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Behaviors/AuditBehavior.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Behaviors/TransactionBehavior.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Behaviors/LoggingBehavior.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Behaviors/AuthorizationBehavior.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Behaviors/CachingBehavior.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Messaging/IIntegrationEvent.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Messaging/IOutboxWriter.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Pagination/KeysetPage.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Application/Pagination/PageRequest.cs"
)

# 4.3 Rge.BuildingBlocks.Infrastructure
$infraBbCsproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Rge.BuildingBlocks.SharedKernel\Rge.BuildingBlocks.SharedKernel.csproj" />
    <ProjectReference Include="..\Rge.BuildingBlocks.Application\Rge.BuildingBlocks.Application.csproj" />
  </ItemGroup>
</Project>
"@
Write-Utf8File "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Rge.BuildingBlocks.Infrastructure.csproj" $infraBbCsproj

Touch-Files @(
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Persistence/NpgsqlDataSourceFactory.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Persistence/ConnectionScope.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Persistence/DapperTypeHandlers.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Persistence/UnitOfWork.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Persistence/RetryPolicy.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Persistence/SqlTimeouts.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Outbox/OutboxStore.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Outbox/OutboxDispatcher.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Outbox/OutboxWorker.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Outbox/OutboxLeaseManager.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Caching/HybridCacheKeys.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Caching/CacheInvalidator.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Resilience/HttpResiliencePipelines.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Observability/ActivitySources.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Observability/Meters.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Observability/LogEnrichers.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Crypto/HashService.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Crypto/ConstantTimeComparer.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Crypto/HmacVerifier.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Crypto/CanonicalJson.cs"
)

# 4.4 Rge.BuildingBlocks.Api
$apiBbCsproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\Rge.BuildingBlocks.SharedKernel\Rge.BuildingBlocks.SharedKernel.csproj" />
    <ProjectReference Include="..\Rge.BuildingBlocks.Application\Rge.BuildingBlocks.Application.csproj" />
  </ItemGroup>
</Project>
"@
Write-Utf8File "src/BuildingBlocks/Rge.BuildingBlocks.Api/Rge.BuildingBlocks.Api.csproj" $apiBbCsproj

Touch-Files @(
  "src/BuildingBlocks/Rge.BuildingBlocks.Api/Endpoints/IEndpointModule.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Api/Endpoints/EndpointGroupExtensions.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Api/Filters/ValidationFilter.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Api/Filters/IdempotencyFilter.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Api/Http/ProblemDetailsFactory.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Api/Http/ResultToHttpMapper.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Api/Serialization/AppJsonContext.cs",
  "src/BuildingBlocks/Rge.BuildingBlocks.Api/Serialization/JsonDefaults.cs"
)

# ==============================================================================
# STEP 5: Modules (8 Business Modules)
# ==============================================================================
Write-Host "[5/9] Scaffolding src/Modules (Payments, Receipts, Finance, Identity, Audit, CostAllocation, Documents, Integrations)..."

$modules = @("Payments", "Receipts", "Finance", "Identity", "Audit", "CostAllocation", "Documents", "Integrations")

foreach ($mod in $modules) {
    Create-ContractsCsproj "src/Modules/$mod/Rge.$mod.Contracts/Rge.$mod.Contracts.csproj"
    Create-DomainCsproj    "src/Modules/$mod/Rge.$mod.Domain/Rge.$mod.Domain.csproj"
    Create-AppCsproj       $mod
    Create-InfraCsproj     $mod
    Create-ApiCsproj       $mod
}

# 5.1 Payments
$payDir = "src/Modules/Payments"
Touch-Files @(
  "$payDir/Rge.Payments.Contracts/Events/OrderPaidIntegrationEvent.cs",
  "$payDir/Rge.Payments.Contracts/Events/PaymentFailedIntegrationEvent.cs",
  "$payDir/Rge.Payments.Contracts/Abstractions/IOrderLookup.cs",
  "$payDir/Rge.Payments.Contracts/Abstractions/IPaymentStatusQuery.cs",

  "$payDir/Rge.Payments.Domain/Orders/Order.cs",
  "$payDir/Rge.Payments.Domain/Orders/OrderStatus.cs",
  "$payDir/Rge.Payments.Domain/Orders/OrderCategory.cs",
  "$payDir/Rge.Payments.Domain/Orders/OrderNumber.cs",
  "$payDir/Rge.Payments.Domain/Orders/OrderLineItem.cs",
  "$payDir/Rge.Payments.Domain/Orders/Events/OrderCreated.cs",
  "$payDir/Rge.Payments.Domain/Orders/Events/OrderPaid.cs",
  "$payDir/Rge.Payments.Domain/Orders/Events/OrderCancelled.cs",
  "$payDir/Rge.Payments.Domain/Orders/Events/OrderRefunded.cs",
  "$payDir/Rge.Payments.Domain/PaymentAttempts/PaymentAttempt.cs",
  "$payDir/Rge.Payments.Domain/PaymentAttempts/PaymentAttemptStatus.cs",
  "$payDir/Rge.Payments.Domain/PaymentAttempts/PaymentAttemptStateMachine.cs",
  "$payDir/Rge.Payments.Domain/PaymentAttempts/PaymentMethod.cs",
  "$payDir/Rge.Payments.Domain/PaymentAttempts/IdempotencyKey.cs",
  "$payDir/Rge.Payments.Domain/PaymentAttempts/FailureCode.cs",
  "$payDir/Rge.Payments.Domain/Webhooks/WebhookEvent.cs",
  "$payDir/Rge.Payments.Domain/Webhooks/WebhookProcessingStatus.cs",
  "$payDir/Rge.Payments.Domain/Webhooks/WebhookEventKey.cs",
  "$payDir/Rge.Payments.Domain/Pricing/IPricingPolicy.cs",
  "$payDir/Rge.Payments.Domain/Pricing/ServerAuthoritativePricing.cs",
  "$payDir/Rge.Payments.Domain/Errors/PaymentErrors.cs",
  "$payDir/Rge.Payments.Domain/Errors/OrderErrors.cs",

  "$payDir/Rge.Payments.Application/Orders/CreateOrder/CreateOrderCommand.cs",
  "$payDir/Rge.Payments.Application/Orders/CreateOrder/CreateOrderHandler.cs",
  "$payDir/Rge.Payments.Application/Orders/CreateOrder/CreateOrderValidator.cs",
  "$payDir/Rge.Payments.Application/Orders/CreateOrder/CreateOrderResponse.cs",
  "$payDir/Rge.Payments.Application/Orders/GetOrder/GetOrderQuery.cs",
  "$payDir/Rge.Payments.Application/Orders/GetOrder/GetOrderHandler.cs",
  "$payDir/Rge.Payments.Application/Orders/GetOrder/GetOrderResponse.cs",
  "$payDir/Rge.Payments.Application/Orders/ListOrders/ListOrdersQuery.cs",
  "$payDir/Rge.Payments.Application/Orders/ListOrders/ListOrdersHandler.cs",
  "$payDir/Rge.Payments.Application/Orders/ListOrders/ListOrdersResponse.cs",
  "$payDir/Rge.Payments.Application/PaymentAttempts/InitiateAttempt/InitiateAttemptCommand.cs",
  "$payDir/Rge.Payments.Application/PaymentAttempts/InitiateAttempt/InitiateAttemptHandler.cs",
  "$payDir/Rge.Payments.Application/PaymentAttempts/InitiateAttempt/InitiateAttemptValidator.cs",
  "$payDir/Rge.Payments.Application/PaymentAttempts/PollAndReconcile/PollAndReconcileQuery.cs",
  "$payDir/Rge.Payments.Application/PaymentAttempts/PollAndReconcile/PollAndReconcileHandler.cs",
  "$payDir/Rge.Payments.Application/PaymentAttempts/ExpireAttempt/ExpireAttemptCommand.cs",
  "$payDir/Rge.Payments.Application/PaymentAttempts/ExpireAttempt/ExpireAttemptHandler.cs",
  "$payDir/Rge.Payments.Application/PaymentAttempts/FailAttempt/FailAttemptCommand.cs",
  "$payDir/Rge.Payments.Application/PaymentAttempts/FailAttempt/FailAttemptHandler.cs",
  "$payDir/Rge.Payments.Application/Webhooks/ReceivePayMongoWebhook/ReceivePayMongoWebhookCommand.cs",
  "$payDir/Rge.Payments.Application/Webhooks/ReceivePayMongoWebhook/ReceivePayMongoWebhookHandler.cs",
  "$payDir/Rge.Payments.Application/Webhooks/ProcessWebhookEvent/ProcessWebhookEventHandler.cs",
  "$payDir/Rge.Payments.Application/Reconciliation/ReconcileOne/ReconcileOneCommand.cs",
  "$payDir/Rge.Payments.Application/Reconciliation/ReconcileOne/ReconcileOneHandler.cs",
  "$payDir/Rge.Payments.Application/Reconciliation/ReconcileAllNonTerminal/ReconcileAllNonTerminalCommand.cs",
  "$payDir/Rge.Payments.Application/Reconciliation/ReconcileAllNonTerminal/ReconcileAllNonTerminalHandler.cs",
  "$payDir/Rge.Payments.Application/Sandbox/SimulateWebhook/SimulateWebhookCommand.cs",
  "$payDir/Rge.Payments.Application/Sandbox/SimulateWebhook/SimulateWebhookHandler.cs",
  "$payDir/Rge.Payments.Application/Sandbox/CreateAndExpireTest/CreateAndExpireTestCommand.cs",
  "$payDir/Rge.Payments.Application/Sandbox/CreateAndExpireTest/CreateAndExpireTestHandler.cs",
  "$payDir/Rge.Payments.Application/Ports/IPaymentGateway.cs",
  "$payDir/Rge.Payments.Application/Ports/IOrderRepository.cs",
  "$payDir/Rge.Payments.Application/Ports/IPaymentAttemptRepository.cs",
  "$payDir/Rge.Payments.Application/Ports/IWebhookEventStore.cs",
  "$payDir/Rge.Payments.Application/Ports/IProductCatalog.cs",
  "$payDir/Rge.Payments.Application/Mapping/PaymentMappings.cs",

  "$payDir/Rge.Payments.Infrastructure/Persistence/Repositories/OrderRepository.cs",
  "$payDir/Rge.Payments.Infrastructure/Persistence/Repositories/PaymentAttemptRepository.cs",
  "$payDir/Rge.Payments.Infrastructure/Persistence/Repositories/WebhookEventStore.cs",
  "$payDir/Rge.Payments.Infrastructure/Persistence/Sql/OrderQueries.cs",
  "$payDir/Rge.Payments.Infrastructure/Persistence/Sql/AttemptQueries.cs",
  "$payDir/Rge.Payments.Infrastructure/Persistence/Mappings/OrderMaterializer.cs",
  "$payDir/Rge.Payments.Infrastructure/Persistence/Mappings/PaymentAttemptMaterializer.cs",
  "$payDir/Rge.Payments.Infrastructure/PayMongo/PayMongoClient.cs",
  "$payDir/Rge.Payments.Infrastructure/PayMongo/PayMongoSignatureVerifier.cs",
  "$payDir/Rge.Payments.Infrastructure/PayMongo/PayMongoDtos.cs",
  "$payDir/Rge.Payments.Infrastructure/PayMongo/PayMongoOptions.cs",
  "$payDir/Rge.Payments.Infrastructure/Catalog/JsonProductCatalog.cs",
  "$payDir/Rge.Payments.Infrastructure/Outbox/OrderPaidOutboxPublisher.cs",
  "$payDir/Rge.Payments.Infrastructure/DependencyInjection.cs",

  "$payDir/Rge.Payments.Api/PaymentsEndpoints.cs",
  "$payDir/Rge.Payments.Api/Orders/CreateOrderEndpoint.cs",
  "$payDir/Rge.Payments.Api/Orders/GetOrderEndpoint.cs",
  "$payDir/Rge.Payments.Api/Orders/ListOrdersEndpoint.cs",
  "$payDir/Rge.Payments.Api/Attempts/InitiateAttemptEndpoint.cs",
  "$payDir/Rge.Payments.Api/Attempts/GetPaymentEndpoint.cs",
  "$payDir/Rge.Payments.Api/Attempts/ExpireEndpoint.cs",
  "$payDir/Rge.Payments.Api/Attempts/FailEndpoint.cs",
  "$payDir/Rge.Payments.Api/Webhooks/PayMongoWebhookEndpoint.cs",
  "$payDir/Rge.Payments.Api/Admin/ReconcileEndpoints.cs",
  "$payDir/Rge.Payments.Api/Admin/WebhookEventsEndpoint.cs",
  "$payDir/Rge.Payments.Api/Admin/OutboxEndpoints.cs",
  "$payDir/Rge.Payments.Api/Sandbox/SandboxEndpoints.cs",
  "$payDir/Rge.Payments.Api/Contracts/PaymentsContracts.cs"
)

# 5.2 Receipts
$recDir = "src/Modules/Receipts"
Touch-Files @(
  "$recDir/Rge.Receipts.Domain/OfficialReceipt.cs",
  "$recDir/Rge.Receipts.Domain/OfficialReceiptNumber.cs",
  "$recDir/Rge.Receipts.Domain/TreasuryAcknowledgement.cs",
  "$recDir/Rge.Receipts.Domain/VatBreakdown.cs",
  "$recDir/Rge.Receipts.Domain/AuditDigest.cs",
  "$recDir/Rge.Receipts.Domain/Policies/BirRmc77_2024Policy.cs",
  "$recDir/Rge.Receipts.Domain/Policies/RoundingPolicy.cs",

  "$recDir/Rge.Receipts.Application/IssueReceipt/IssueReceiptHandler.cs",
  "$recDir/Rge.Receipts.Application/GetReceiptByPayment/GetReceiptByPaymentQuery.cs",
  "$recDir/Rge.Receipts.Application/GetReceiptByPayment/GetReceiptByPaymentHandler.cs",
  "$recDir/Rge.Receipts.Application/GetReceiptByOrder/GetReceiptByOrderQuery.cs",
  "$recDir/Rge.Receipts.Application/GetReceiptByOrder/GetReceiptByOrderHandler.cs",
  "$recDir/Rge.Receipts.Application/RenderReceiptPdf/RenderReceiptPdfQuery.cs",
  "$recDir/Rge.Receipts.Application/RenderReceiptPdf/RenderReceiptPdfHandler.cs",
  "$recDir/Rge.Receipts.Application/ResendReceiptEmail/ResendReceiptEmailCommand.cs",
  "$recDir/Rge.Receipts.Application/ResendReceiptEmail/ResendReceiptEmailHandler.cs",
  "$recDir/Rge.Receipts.Application/Ports/IReceiptRepository.cs",
  "$recDir/Rge.Receipts.Application/Ports/IPdfRenderer.cs",
  "$recDir/Rge.Receipts.Application/Ports/IReceiptMailer.cs",
  "$recDir/Rge.Receipts.Application/Ports/ICanonicalJsonSerializer.cs",

  "$recDir/Rge.Receipts.Infrastructure/Persistence/ReceiptRepository.cs",
  "$recDir/Rge.Receipts.Infrastructure/Persistence/Sql/ReceiptQueries.cs",
  "$recDir/Rge.Receipts.Infrastructure/Pdf/QuestPdfReceiptRenderer.cs",
  "$recDir/Rge.Receipts.Infrastructure/Pdf/ReceiptLayout.cs",
  "$recDir/Rge.Receipts.Infrastructure/Pdf/FontAssets/.gitkeep",
  "$recDir/Rge.Receipts.Infrastructure/Mail/SmtpReceiptMailer.cs",
  "$recDir/Rge.Receipts.Infrastructure/Mail/EmailTemplates/.gitkeep",
  "$recDir/Rge.Receipts.Infrastructure/Digest/CanonicalJsonSerializer.cs",
  "$recDir/Rge.Receipts.Infrastructure/Digest/DigestService.cs",
  "$recDir/Rge.Receipts.Infrastructure/DependencyInjection.cs",

  "$recDir/Rge.Receipts.Api/ReceiptEndpoints.cs",
  "$recDir/Rge.Receipts.Api/Contracts/ReceiptContracts.cs"
)

# 5.3 Finance
$finDir = "src/Modules/Finance"
Touch-Files @(
  "$finDir/Rge.Finance.Contracts/IAdvanceBalanceQuery.cs",
  "$finDir/Rge.Finance.Contracts/ISodGuard.cs",
  "$finDir/Rge.Finance.Contracts/AdvanceClosedIntegrationEvent.cs",

  "$finDir/Rge.Finance.Domain/CashAdvances/CashAdvance.cs",
  "$finDir/Rge.Finance.Domain/CashAdvances/StateMachines/ApprovalStatus.cs",
  "$finDir/Rge.Finance.Domain/CashAdvances/StateMachines/PaymentStatus.cs",
  "$finDir/Rge.Finance.Domain/CashAdvances/StateMachines/LiquidationReviewStatus.cs",
  "$finDir/Rge.Finance.Domain/CashAdvances/StateMachines/SettlementStatus.cs",
  "$finDir/Rge.Finance.Domain/CashAdvances/StateMachines/PostingStatus.cs",
  "$finDir/Rge.Finance.Domain/CashAdvances/StateMachines/StateTransitionTables.cs",
  "$finDir/Rge.Finance.Domain/CashAdvances/AdvanceNumber.cs",
  "$finDir/Rge.Finance.Domain/CashAdvances/PurposeCode.cs",
  "$finDir/Rge.Finance.Domain/CashAdvances/Events/CashAdvanceEvents.cs",
  "$finDir/Rge.Finance.Domain/Disbursements/Disbursement.cs",
  "$finDir/Rge.Finance.Domain/Disbursements/DisbursementStage.cs",
  "$finDir/Rge.Finance.Domain/Disbursements/FourEyesRule.cs",
  "$finDir/Rge.Finance.Domain/Disbursements/BankReference.cs",
  "$finDir/Rge.Finance.Domain/Ledger/AdvanceLedgerEntry.cs",
  "$finDir/Rge.Finance.Domain/Ledger/LedgerEntryType.cs",
  "$finDir/Rge.Finance.Domain/Ledger/AdvanceBalanceCalculator.cs",
  "$finDir/Rge.Finance.Domain/Liquidation/ExpenseReport.cs",
  "$finDir/Rge.Finance.Domain/Liquidation/ExpenseLine.cs",
  "$finDir/Rge.Finance.Domain/Liquidation/DuplicateFingerprint.cs",
  "$finDir/Rge.Finance.Domain/Liquidation/SettlementApplication.cs",
  "$finDir/Rge.Finance.Domain/Liquidation/ClosingSnapshot.cs",
  "$finDir/Rge.Finance.Domain/Policy/FinancialPolicy.cs",
  "$finDir/Rge.Finance.Domain/Policy/PolicyVersion.cs",
  "$finDir/Rge.Finance.Domain/Policy/PolicyRule.cs",
  "$finDir/Rge.Finance.Domain/Policy/PeriodCounter.cs",
  "$finDir/Rge.Finance.Domain/Policy/ExceptionRequest.cs",
  "$finDir/Rge.Finance.Domain/Policy/PolicyEvaluator.cs",
  "$finDir/Rge.Finance.Domain/Procurement/PurchaseRequest.cs",
  "$finDir/Rge.Finance.Domain/Procurement/PurchaseRequestLine.cs",
  "$finDir/Rge.Finance.Domain/Procurement/AccountPayable.cs",
  "$finDir/Rge.Finance.Domain/Procurement/NormalizedInvoiceNumber.cs",
  "$finDir/Rge.Finance.Domain/Procurement/ThreeWayMatch.cs",
  "$finDir/Rge.Finance.Domain/Procurement/PaymentRecord.cs",
  "$finDir/Rge.Finance.Domain/Vendors/Vendor.cs",
  "$finDir/Rge.Finance.Domain/Vendors/VendorBankChange.cs",
  "$finDir/Rge.Finance.Domain/Vendors/MaskedBankAccount.cs",
  "$finDir/Rge.Finance.Domain/Vendors/TaxCode.cs",
  "$finDir/Rge.Finance.Domain/Exceptions/ExceptionRegisterItem.cs",
  "$finDir/Rge.Finance.Domain/Approvals/ApprovalMatrix.cs",
  "$finDir/Rge.Finance.Domain/Approvals/ApprovalHistoryEntry.cs",
  "$finDir/Rge.Finance.Domain/Approvals/ApprovalRouter.cs",
  "$finDir/Rge.Finance.Domain/Errors/FinanceErrors.cs"
)

Create-VerticalSlices "$finDir/Rge.Finance.Application/CashAdvances" @("Create", "Submit", "Decide", "GetById", "List", "CashReturn", "ReimbursementSettle", "Close")
Create-VerticalSlices "$finDir/Rge.Finance.Application/Disbursements" @("Prepare", "Verify", "BankApprove", "Execute", "VerifyEvidence", "SetExceptionState")
Create-VerticalSlices "$finDir/Rge.Finance.Application/ExpenseReports" @("Create", "Patch", "AddLines", "Evaluate", "Submit", "Review", "GetById", "List")
Create-VerticalSlices "$finDir/Rge.Finance.Application/Policies" @("AuthorVersion", "SubmitVersion", "ApproveVersion", "RetireVersion", "List")
Create-VerticalSlices "$finDir/Rge.Finance.Application/ExceptionRequests" @("Submit", "Decide", "List")
Create-VerticalSlices "$finDir/Rge.Finance.Application/Vendors" @("Register", "Verify", "RequestBankChange", "DecideBankChange", "List")
Create-VerticalSlices "$finDir/Rge.Finance.Application/TaxCodes" @("Register", "List")
Create-VerticalSlices "$finDir/Rge.Finance.Application/PurchaseRequests" @("Create", "Submit", "Decide", "GetById", "List")
Create-VerticalSlices "$finDir/Rge.Finance.Application/Payables" @("Register", "PreparePayment", "VerifyPayment", "BankApprovePayment", "ExecutePayment", "ReconcilePayment", "SetPaymentExceptionState")
Create-VerticalSlices "$finDir/Rge.Finance.Application/Reporting" @("DashboardSummary", "KpiDefinitions", "BusinessConfirmations", "Tracker", "TrackerCsvExport", "AgingBuckets", "ExceptionRegister")

Touch-Files @(
  "$finDir/Rge.Finance.Application/Security/SodGuard.cs",
  "$finDir/Rge.Finance.Application/Security/ApprovalLimitGuard.cs",
  "$finDir/Rge.Finance.Application/Security/ScopeGuard.cs",
  "$finDir/Rge.Finance.Application/Ports/IFinanceRepositories.cs",

  "$finDir/Rge.Finance.Infrastructure/Persistence/Repositories/.gitkeep",
  "$finDir/Rge.Finance.Infrastructure/Persistence/Sql/.gitkeep",
  "$finDir/Rge.Finance.Infrastructure/Persistence/ReadModels/.gitkeep",
  "$finDir/Rge.Finance.Infrastructure/Reporting/DashboardReadService.cs",
  "$finDir/Rge.Finance.Infrastructure/Reporting/TrackerReadService.cs",
  "$finDir/Rge.Finance.Infrastructure/Reporting/CsvInjectionSafeWriter.cs",
  "$finDir/Rge.Finance.Infrastructure/Policy/PolicyCache.cs",
  "$finDir/Rge.Finance.Infrastructure/Policy/PolicyCacheInvalidator.cs",
  "$finDir/Rge.Finance.Infrastructure/Calendar/PhHolidayCalendarProvider.cs",
  "$finDir/Rge.Finance.Infrastructure/DependencyInjection.cs",

  "$finDir/Rge.Finance.Api/FinanceEndpoints.cs",
  "$finDir/Rge.Finance.Api/CashAdvances/CashAdvanceEndpoints.cs",
  "$finDir/Rge.Finance.Api/Disbursements/DisbursementEndpoints.cs",
  "$finDir/Rge.Finance.Api/ExpenseReports/ExpenseReportEndpoints.cs",
  "$finDir/Rge.Finance.Api/Policies/PolicyEndpoints.cs",
  "$finDir/Rge.Finance.Api/Exceptions/ExceptionEndpoints.cs",
  "$finDir/Rge.Finance.Api/Vendors/VendorEndpoints.cs",
  "$finDir/Rge.Finance.Api/Procurement/ProcurementEndpoints.cs",
  "$finDir/Rge.Finance.Api/Payables/PayableEndpoints.cs",
  "$finDir/Rge.Finance.Api/Reporting/ReportingEndpoints.cs",
  "$finDir/Rge.Finance.Api/Contracts/FinanceContracts.cs"
)

# 5.4 Identity
$idDir = "src/Modules/Identity"
Touch-Files @(
  "$idDir/Rge.Identity.Domain/Actor.cs",
  "$idDir/Rge.Identity.Domain/Role.cs",
  "$idDir/Rge.Identity.Domain/RoleAssignment.cs",
  "$idDir/Rge.Identity.Domain/Session.cs",
  "$idDir/Rge.Identity.Domain/OAuthState.cs",
  "$idDir/Rge.Identity.Domain/LockoutPolicy.cs"
)

Create-VerticalSlices "$idDir/Rge.Identity.Application" @("Login", "Logout", "GetSession", "CreateCsrfToken", "OAuthBegin", "OAuthCallback", "ResolveActor", "ListFinanceUsers", "SwitchUser", "ActAs")

Touch-Files @(
  "$idDir/Rge.Identity.Application/Ports/IIdentityPorts.cs",
  "$idDir/Rge.Identity.Infrastructure/PgAuthenticator.cs",
  "$idDir/Rge.Identity.Infrastructure/PgOAuthStateStore.cs",
  "$idDir/Rge.Identity.Infrastructure/SessionTokenHasher.cs",
  "$idDir/Rge.Identity.Infrastructure/CsrfTokenService.cs",
  "$idDir/Rge.Identity.Infrastructure/ActorCache.cs",
  "$idDir/Rge.Identity.Infrastructure/RoleStore.cs",
  "$idDir/Rge.Identity.Infrastructure/DependencyInjection.cs",
  "$idDir/Rge.Identity.Api/AuthEndpoints.cs",
  "$idDir/Rge.Identity.Api/FinanceIdentityEndpoints.cs",
  "$idDir/Rge.Identity.Api/Authentication/SessionAuthHandler.cs",
  "$idDir/Rge.Identity.Api/Authentication/BearerAuthHandler.cs",
  "$idDir/Rge.Identity.Api/Authorization/PermissionRequirement.cs",
  "$idDir/Rge.Identity.Api/Authorization/SodRequirement.cs",
  "$idDir/Rge.Identity.Api/Authorization/ScopeRequirement.cs",
  "$idDir/Rge.Identity.Api/Authorization/Handlers/AuthorizationHandlers.cs",
  "$idDir/Rge.Identity.Api/Authorization/PolicyNames.cs",
  "$idDir/Rge.Identity.Api/Authorization/PermissionCatalog.cs"
)

# 5.5 Audit
$audDir = "src/Modules/Audit"
Touch-Files @(
  "$audDir/Rge.Audit.Contracts/IAuditTrail.cs",
  "$audDir/Rge.Audit.Domain/AuditEvent.cs",
  "$audDir/Rge.Audit.Domain/AuditHash.cs",
  "$audDir/Rge.Audit.Domain/AuditAction.cs"
)

Create-VerticalSlices "$audDir/Rge.Audit.Application" @("AppendAuditEvent", "QueryAuditEvents", "VerifyChain")

Touch-Files @(
  "$audDir/Rge.Audit.Infrastructure/PgAuditAppender.cs",
  "$audDir/Rge.Audit.Infrastructure/ChainVerifier.cs",
  "$audDir/Rge.Audit.Infrastructure/AuditQueryReader.cs",
  "$audDir/Rge.Audit.Infrastructure/DependencyInjection.cs",
  "$audDir/Rge.Audit.Api/AuditEndpoints.cs"
)

# 5.6 CostAllocation
$costDir = "src/Modules/CostAllocation"
Touch-Files @(
  "$costDir/Rge.CostAllocation.Domain/CostDepartment.cs",
  "$costDir/Rge.CostAllocation.Domain/CostProduct.cs",
  "$costDir/Rge.CostAllocation.Domain/CostActivity.cs",
  "$costDir/Rge.CostAllocation.Domain/CostDriver.cs",
  "$costDir/Rge.CostAllocation.Domain/BudgetCap.cs",
  "$costDir/Rge.CostAllocation.Domain/AllocationCalculator.cs"
)

Create-VerticalSlices "$costDir/Rge.CostAllocation.Application" @("GetCostAllocation", "UpdateDepartmentCost")

Touch-Files @(
  "$costDir/Rge.CostAllocation.Application/Ports/ICostAllocationPorts.cs",
  "$costDir/Rge.CostAllocation.Infrastructure/JsonConfigurationStore.cs",
  "$costDir/Rge.CostAllocation.Infrastructure/LegacyDashboardReader.cs",
  "$costDir/Rge.CostAllocation.Infrastructure/DependencyInjection.cs",
  "$costDir/Rge.CostAllocation.Api/CostAllocationEndpoints.cs",
  "$costDir/Rge.CostAllocation.Api/LegacyDashboardEndpoints.cs",
  "$costDir/Rge.CostAllocation.Api/ConfigurationEndpoints.cs"
)

# 5.7 Documents
$docDir = "src/Modules/Documents"
Touch-Files @(
  "$docDir/Rge.Documents.Domain/DocumentReference.cs",
  "$docDir/Rge.Documents.Domain/MimeSignature.cs",
  "$docDir/Rge.Documents.Domain/FileSizePolicy.cs"
)

Create-VerticalSlices "$docDir/Rge.Documents.Application" @("UploadDocument", "ListDocuments", "DownloadDocument")

Touch-Files @(
  "$docDir/Rge.Documents.Application/Ports/IDocumentPorts.cs",
  "$docDir/Rge.Documents.Infrastructure/MagicByteValidator.cs",
  "$docDir/Rge.Documents.Infrastructure/Sha256Hasher.cs",
  "$docDir/Rge.Documents.Infrastructure/SafeFileStore.cs",
  "$docDir/Rge.Documents.Infrastructure/AntivirusScanHook.cs",
  "$docDir/Rge.Documents.Infrastructure/PathTraversalGuard.cs",
  "$docDir/Rge.Documents.Infrastructure/DependencyInjection.cs",
  "$docDir/Rge.Documents.Api/DocumentEndpoints.cs"
)

# 5.8 Integrations
$intDir = "src/Modules/Integrations"
Touch-Files @(
  "$intDir/Rge.Integrations.Domain/GoogleFormSubmission.cs",
  "$intDir/Rge.Integrations.Domain/SheetSchema18.cs",
  "$intDir/Rge.Integrations.Domain/HeaderAliasMap.cs"
)

Create-VerticalSlices "$intDir/Rge.Integrations.Application" @("SubmitGoogleForm", "GetFormStatus", "IntegrationStatusProbe", "TestPayMongo", "TestSmtp", "SaveCredentials", "SheetSyncHandler")

Touch-Files @(
  "$intDir/Rge.Integrations.Infrastructure/GoogleSheetsClient.cs",
  "$intDir/Rge.Integrations.Infrastructure/SheetRowMapper.cs",
  "$intDir/Rge.Integrations.Infrastructure/SmtpClientFactory.cs",
  "$intDir/Rge.Integrations.Infrastructure/FormSecretVerifier.cs",
  "$intDir/Rge.Integrations.Infrastructure/SecretStoreWriter.cs",
  "$intDir/Rge.Integrations.Infrastructure/DependencyInjection.cs",
  "$intDir/Rge.Integrations.Api/GoogleFormsEndpoints.cs",
  "$intDir/Rge.Integrations.Api/IntegrationEndpoints.cs"
)

# ==============================================================================
# STEP 6: Tests (Cross-Cutting & Module Test Suites)
# ==============================================================================
Write-Host "[6/9] Scaffolding tests/..."

$crossTests = @(
  "Architecture.Tests",
  "Contract.Parity.Tests",
  "Security.Tests",
  "Performance.Tests",
  "Integration.Tests",
  "Tests.Common"
)

foreach ($t in $crossTests) {
    Create-TestCsproj "tests/$t/Rge.$t.csproj"
}

$moduleTests = @(
  "Payments.Domain.Tests",
  "Payments.Application.Tests",
  "Payments.Integration.Tests",
  "Receipts.Domain.Tests",
  "Receipts.Application.Tests",
  "Receipts.Integration.Tests",
  "Finance.Domain.Tests",
  "Finance.Application.Tests",
  "Finance.Integration.Tests",
  "Identity.Domain.Tests",
  "Identity.Application.Tests",
  "Identity.Integration.Tests",
  "Audit.Domain.Tests",
  "Audit.Integration.Tests",
  "CostAllocation.Domain.Tests",
  "CostAllocation.Application.Tests",
  "Documents.Domain.Tests",
  "Documents.Integration.Tests",
  "Integrations.Application.Tests",
  "Integrations.Integration.Tests"
)

foreach ($mt in $moduleTests) {
    Create-TestCsproj "tests/Modules/$mt/Rge.$mt.csproj"
}

# ==============================================================================
# STEP 7: Migration & Verification Tools (tools/)
# ==============================================================================
Write-Host "[7/9] Scaffolding tools/..."

$tools = @(
  "GoldenMaster.Exporter",
  "ParityRunner",
  "DataMigration.Verifier",
  "AuditChain.Verifier"
)

foreach ($tool in $tools) {
    Create-ToolCsproj "tools/$tool/Rge.$tool.csproj"
    Touch-Files @("tools/$tool/Program.cs")
}

# ==============================================================================
# STEP 8: Deploy & GitHub Actions Workflows
# ==============================================================================
Write-Host "[8/9] Scaffolding deploy/ and .github/workflows/..."

Touch-Files @(
  "deploy/docker/Dockerfile",
  "deploy/docker/Dockerfile.migrator",
  "deploy/docker/docker-compose.yml",
  "deploy/docker/docker-compose.test.yml",
  "deploy/k8s/deployment.yaml",
  "deploy/k8s/hpa.yaml",
  "deploy/k8s/networkpolicy.yaml",
  "deploy/k8s/pdb.yaml",
  "deploy/k8s/secrets-provider-class.yaml",
  "deploy/proxy/yarp-strangler-routes.json",
  "deploy/proxy/nginx-strangler.conf",
  "deploy/observability/otel-collector.yaml",
  "deploy/observability/grafana-dashboards.json",
  "deploy/observability/alert-rules.yaml",

  ".github/workflows/ci-build-test.yml",
  ".github/workflows/ci-security-scan.yml",
  ".github/workflows/ci-parity.yml",
  ".github/workflows/ci-performance-gate.yml",
  ".github/workflows/cd-release.yml"
)

# ==============================================================================
# STEP 9: Generate Rge.Accounting.slnx (.NET 10 XML Solution File)
# ==============================================================================
Write-Host "[9/9] Generating $SolutionName.slnx..."

$slnx = @"
<Solution>
  <Folder Name="/src/" />
  <Folder Name="/src/Host/">
    <Project Path="src/Host/Rge.Accounting.Host/Rge.Accounting.Host.csproj" />
  </Folder>
  <Folder Name="/src/BuildingBlocks/">
    <Project Path="src/BuildingBlocks/Rge.BuildingBlocks.SharedKernel/Rge.BuildingBlocks.SharedKernel.csproj" />
    <Project Path="src/BuildingBlocks/Rge.BuildingBlocks.Application/Rge.BuildingBlocks.Application.csproj" />
    <Project Path="src/BuildingBlocks/Rge.BuildingBlocks.Infrastructure/Rge.BuildingBlocks.Infrastructure.csproj" />
    <Project Path="src/BuildingBlocks/Rge.BuildingBlocks.Api/Rge.BuildingBlocks.Api.csproj" />
  </Folder>
  <Folder Name="/src/Modules/" />
  <Folder Name="/src/Modules/Payments/">
    <Project Path="src/Modules/Payments/Rge.Payments.Contracts/Rge.Payments.Contracts.csproj" />
    <Project Path="src/Modules/Payments/Rge.Payments.Domain/Rge.Payments.Domain.csproj" />
    <Project Path="src/Modules/Payments/Rge.Payments.Application/Rge.Payments.Application.csproj" />
    <Project Path="src/Modules/Payments/Rge.Payments.Infrastructure/Rge.Payments.Infrastructure.csproj" />
    <Project Path="src/Modules/Payments/Rge.Payments.Api/Rge.Payments.Api.csproj" />
  </Folder>
  <Folder Name="/src/Modules/Receipts/">
    <Project Path="src/Modules/Receipts/Rge.Receipts.Contracts/Rge.Receipts.Contracts.csproj" />
    <Project Path="src/Modules/Receipts/Rge.Receipts.Domain/Rge.Receipts.Domain.csproj" />
    <Project Path="src/Modules/Receipts/Rge.Receipts.Application/Rge.Receipts.Application.csproj" />
    <Project Path="src/Modules/Receipts/Rge.Receipts.Infrastructure/Rge.Receipts.Infrastructure.csproj" />
    <Project Path="src/Modules/Receipts/Rge.Receipts.Api/Rge.Receipts.Api.csproj" />
  </Folder>
  <Folder Name="/src/Modules/Finance/">
    <Project Path="src/Modules/Finance/Rge.Finance.Contracts/Rge.Finance.Contracts.csproj" />
    <Project Path="src/Modules/Finance/Rge.Finance.Domain/Rge.Finance.Domain.csproj" />
    <Project Path="src/Modules/Finance/Rge.Finance.Application/Rge.Finance.Application.csproj" />
    <Project Path="src/Modules/Finance/Rge.Finance.Infrastructure/Rge.Finance.Infrastructure.csproj" />
    <Project Path="src/Modules/Finance/Rge.Finance.Api/Rge.Finance.Api.csproj" />
  </Folder>
  <Folder Name="/src/Modules/Identity/">
    <Project Path="src/Modules/Identity/Rge.Identity.Contracts/Rge.Identity.Contracts.csproj" />
    <Project Path="src/Modules/Identity/Rge.Identity.Domain/Rge.Identity.Domain.csproj" />
    <Project Path="src/Modules/Identity/Rge.Identity.Application/Rge.Identity.Application.csproj" />
    <Project Path="src/Modules/Identity/Rge.Identity.Infrastructure/Rge.Identity.Infrastructure.csproj" />
    <Project Path="src/Modules/Identity/Rge.Identity.Api/Rge.Identity.Api.csproj" />
  </Folder>
  <Folder Name="/src/Modules/Audit/">
    <Project Path="src/Modules/Audit/Rge.Audit.Contracts/Rge.Audit.Contracts.csproj" />
    <Project Path="src/Modules/Audit/Rge.Audit.Domain/Rge.Audit.Domain.csproj" />
    <Project Path="src/Modules/Audit/Rge.Audit.Application/Rge.Audit.Application.csproj" />
    <Project Path="src/Modules/Audit/Rge.Audit.Infrastructure/Rge.Audit.Infrastructure.csproj" />
    <Project Path="src/Modules/Audit/Rge.Audit.Api/Rge.Audit.Api.csproj" />
  </Folder>
  <Folder Name="/src/Modules/CostAllocation/">
    <Project Path="src/Modules/CostAllocation/Rge.CostAllocation.Contracts/Rge.CostAllocation.Contracts.csproj" />
    <Project Path="src/Modules/CostAllocation/Rge.CostAllocation.Domain/Rge.CostAllocation.Domain.csproj" />
    <Project Path="src/Modules/CostAllocation/Rge.CostAllocation.Application/Rge.CostAllocation.Application.csproj" />
    <Project Path="src/Modules/CostAllocation/Rge.CostAllocation.Infrastructure/Rge.CostAllocation.Infrastructure.csproj" />
    <Project Path="src/Modules/CostAllocation/Rge.CostAllocation.Api/Rge.CostAllocation.Api.csproj" />
  </Folder>
  <Folder Name="/src/Modules/Documents/">
    <Project Path="src/Modules/Documents/Rge.Documents.Contracts/Rge.Documents.Contracts.csproj" />
    <Project Path="src/Modules/Documents/Rge.Documents.Domain/Rge.Documents.Domain.csproj" />
    <Project Path="src/Modules/Documents/Rge.Documents.Application/Rge.Documents.Application.csproj" />
    <Project Path="src/Modules/Documents/Rge.Documents.Infrastructure/Rge.Documents.Infrastructure.csproj" />
    <Project Path="src/Modules/Documents/Rge.Documents.Api/Rge.Documents.Api.csproj" />
  </Folder>
  <Folder Name="/src/Modules/Integrations/">
    <Project Path="src/Modules/Integrations/Rge.Integrations.Contracts/Rge.Integrations.Contracts.csproj" />
    <Project Path="src/Modules/Integrations/Rge.Integrations.Domain/Rge.Integrations.Domain.csproj" />
    <Project Path="src/Modules/Integrations/Rge.Integrations.Application/Rge.Integrations.Application.csproj" />
    <Project Path="src/Modules/Integrations/Rge.Integrations.Infrastructure/Rge.Integrations.Infrastructure.csproj" />
    <Project Path="src/Modules/Integrations/Rge.Integrations.Api/Rge.Integrations.Api.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/Architecture.Tests/Rge.Architecture.Tests.csproj" />
    <Project Path="tests/Contract.Parity.Tests/Rge.Contract.Parity.Tests.csproj" />
    <Project Path="tests/Security.Tests/Rge.Security.Tests.csproj" />
    <Project Path="tests/Performance.Tests/Rge.Performance.Tests.csproj" />
    <Project Path="tests/Integration.Tests/Rge.Integration.Tests.csproj" />
    <Project Path="tests/Tests.Common/Rge.Tests.Common.csproj" />
  </Folder>
  <Folder Name="/tests/Modules/">
    <Project Path="tests/Modules/Payments.Domain.Tests/Rge.Payments.Domain.Tests.csproj" />
    <Project Path="tests/Modules/Payments.Application.Tests/Rge.Payments.Application.Tests.csproj" />
    <Project Path="tests/Modules/Payments.Integration.Tests/Rge.Payments.Integration.Tests.csproj" />
    <Project Path="tests/Modules/Receipts.Domain.Tests/Rge.Receipts.Domain.Tests.csproj" />
    <Project Path="tests/Modules/Receipts.Application.Tests/Rge.Receipts.Application.Tests.csproj" />
    <Project Path="tests/Modules/Receipts.Integration.Tests/Rge.Receipts.Integration.Tests.csproj" />
    <Project Path="tests/Modules/Finance.Domain.Tests/Rge.Finance.Domain.Tests.csproj" />
    <Project Path="tests/Modules/Finance.Application.Tests/Rge.Finance.Application.Tests.csproj" />
    <Project Path="tests/Modules/Finance.Integration.Tests/Rge.Finance.Integration.Tests.csproj" />
    <Project Path="tests/Modules/Identity.Domain.Tests/Rge.Identity.Domain.Tests.csproj" />
    <Project Path="tests/Modules/Identity.Application.Tests/Rge.Identity.Application.Tests.csproj" />
    <Project Path="tests/Modules/Identity.Integration.Tests/Rge.Identity.Integration.Tests.csproj" />
    <Project Path="tests/Modules/Audit.Domain.Tests/Rge.Audit.Domain.Tests.csproj" />
    <Project Path="tests/Modules/Audit.Integration.Tests/Rge.Audit.Integration.Tests.csproj" />
    <Project Path="tests/Modules/CostAllocation.Domain.Tests/Rge.CostAllocation.Domain.Tests.csproj" />
    <Project Path="tests/Modules/CostAllocation.Application.Tests/Rge.CostAllocation.Application.Tests.csproj" />
    <Project Path="tests/Modules/Documents.Domain.Tests/Rge.Documents.Domain.Tests.csproj" />
    <Project Path="tests/Modules/Documents.Integration.Tests/Rge.Documents.Integration.Tests.csproj" />
    <Project Path="tests/Modules/Integrations.Application.Tests/Rge.Integrations.Application.Tests.csproj" />
    <Project Path="tests/Modules/Integrations.Integration.Tests/Rge.Integrations.Integration.Tests.csproj" />
  </Folder>
  <Folder Name="/tools/">
    <Project Path="tools/GoldenMaster.Exporter/Rge.GoldenMaster.Exporter.csproj" />
    <Project Path="tools/ParityRunner/Rge.ParityRunner.csproj" />
    <Project Path="tools/DataMigration.Verifier/Rge.DataMigration.Verifier.csproj" />
    <Project Path="tools/AuditChain.Verifier/Rge.AuditChain.Verifier.csproj" />
  </Folder>
</Solution>
"@
Write-Utf8File "$SolutionName.slnx" $slnx

Write-Host "=============================================================================="
Write-Host " Scaffold complete!"
Write-Host " Root location : $TargetDir"
Write-Host " Solution file : $TargetDir\$SolutionName.slnx"
Write-Host "=============================================================================="
