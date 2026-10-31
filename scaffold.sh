#!/usr/bin/env bash
# ==============================================================================
# R.G.E Accounting Platform - .NET 10 Architecture & Solution Scaffold Script
# Style: Modular Monolith + Clean Architecture + Vertical Slices (Skeleton Only)
# ==============================================================================

set -euo pipefail

# ------------------------------------------------------------------------------
# Configurable Parameters
# ------------------------------------------------------------------------------
# Default target directory. Override by passing a path as the first argument:
#   ./scaffold.sh "/e/Accounting/Backend"
TARGET_DIR="${1:-./Backend}"
DOTNET_SDK_VERSION="10.0.301"
TARGET_FRAMEWORK="net10.0"
SOLUTION_NAME="Rge.Accounting"

echo "=============================================================================="
echo " Scaffolding R.G.E Accounting Platform (.NET 10 Skeleton)"
echo " Target Directory : ${TARGET_DIR}"
echo " Target Framework : ${TARGET_FRAMEWORK}"
echo " Pinned SDK       : ${DOTNET_SDK_VERSION}"
echo "=============================================================================="

mkdir -p "${TARGET_DIR}"
cd "${TARGET_DIR}"

# ------------------------------------------------------------------------------
# Helper Functions
# ------------------------------------------------------------------------------

# Creates parent directories and touches one or more empty skeleton files
touch_files() {
  for filepath in "$@"; do
    mkdir -p "$(dirname "${filepath}")"
    if [[ ! -f "${filepath}" ]]; then
      : > "${filepath}"
    fi
  done
}

# Creates a pure Domain .csproj (References SharedKernel ONLY; zero NuGet deps)
create_domain_csproj() {
  local proj_path="$1"
  mkdir -p "$(dirname "${proj_path}")"
  cat << 'EOF' > "${proj_path}"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\BuildingBlocks\Rge.BuildingBlocks.SharedKernel\Rge.BuildingBlocks.SharedKernel.csproj" />
  </ItemGroup>
</Project>
EOF
}

# Creates a Contracts .csproj (References SharedKernel + BuildingBlocks.Application)
create_contracts_csproj() {
  local proj_path="$1"
  mkdir -p "$(dirname "${proj_path}")"
  cat << 'EOF' > "${proj_path}"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\BuildingBlocks\Rge.BuildingBlocks.SharedKernel\Rge.BuildingBlocks.SharedKernel.csproj" />
    <ProjectReference Include="..\..\..\BuildingBlocks\Rge.BuildingBlocks.Application\Rge.BuildingBlocks.Application.csproj" />
  </ItemGroup>
</Project>
EOF
}

# Creates an Application .csproj (References Module Domain + Contracts + BuildingBlocks.Application)
create_app_csproj() {
  local module_name="$1"
  local proj_path="src/Modules/${module_name}/Rge.${module_name}.Application/Rge.${module_name}.Application.csproj"
  mkdir -p "$(dirname "${proj_path}")"
  cat << EOF > "${proj_path}"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\BuildingBlocks\Rge.BuildingBlocks.Application\Rge.BuildingBlocks.Application.csproj" />
    <ProjectReference Include="..\Rge.${module_name}.Domain\Rge.${module_name}.Domain.csproj" />
    <ProjectReference Include="..\Rge.${module_name}.Contracts\Rge.${module_name}.Contracts.csproj" />
  </ItemGroup>
</Project>
EOF
}

# Creates an Infrastructure .csproj (References Module Application + Domain + BuildingBlocks.Infrastructure)
create_infra_csproj() {
  local module_name="$1"
  local proj_path="src/Modules/${module_name}/Rge.${module_name}.Infrastructure/Rge.${module_name}.Infrastructure.csproj"
  mkdir -p "$(dirname "${proj_path}")"
  cat << EOF > "${proj_path}"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\BuildingBlocks\Rge.BuildingBlocks.Infrastructure\Rge.BuildingBlocks.Infrastructure.csproj" />
    <ProjectReference Include="..\Rge.${module_name}.Application\Rge.${module_name}.Application.csproj" />
    <ProjectReference Include="..\Rge.${module_name}.Domain\Rge.${module_name}.Domain.csproj" />
  </ItemGroup>
</Project>
EOF
}

# Creates an Api .csproj (References Module Application + Contracts + BuildingBlocks.Api)
create_api_csproj() {
  local module_name="$1"
  local proj_path="src/Modules/${module_name}/Rge.${module_name}.Api/Rge.${module_name}.Api.csproj"
  mkdir -p "$(dirname "${proj_path}")"
  cat << EOF > "${proj_path}"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\BuildingBlocks\Rge.BuildingBlocks.Api\Rge.BuildingBlocks.Api.csproj" />
    <ProjectReference Include="..\Rge.${module_name}.Application\Rge.${module_name}.Application.csproj" />
    <ProjectReference Include="..\Rge.${module_name}.Contracts\Rge.${module_name}.Contracts.csproj" />
  </ItemGroup>
</Project>
EOF
}

# Creates a Test .csproj
create_test_csproj() {
  local proj_path="$1"
  mkdir -p "$(dirname "${proj_path}")"
  cat << 'EOF' > "${proj_path}"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
</Project>
EOF
}

# Creates a Console Tool .csproj
create_tool_csproj() {
  local proj_path="$1"
  mkdir -p "$(dirname "${proj_path}")"
  cat << 'EOF' > "${proj_path}"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
EOF
}

# Helper to scaffold vertical slice folders (Command/Query, Handler, Validator, Response)
create_vertical_slices() {
  local base_dir="$1"
  shift
  for slice in "$@"; do
    mkdir -p "${base_dir}/${slice}"
    touch_files "${base_dir}/${slice}/${slice}Handler.cs"
  done
}

# ==============================================================================
# STEP 1: Root Configuration & Governance Files
# ==============================================================================
echo "[1/9] Creating root configuration files..."

cat << EOF > global.json
{
  "sdk": {
    "version": "${DOTNET_SDK_VERSION}",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
EOF

cat << EOF > Directory.Build.props
<Project>
  <PropertyGroup>
    <TargetFramework>${TARGET_FRAMEWORK}</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-all</AnalysisLevel>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <DisableImplicitNuGetFallbackFolder>true</DisableImplicitNuGetFallbackFolder>
  </PropertyGroup>

  <!-- Exclude Sandbox & Dev-Only tools from Release/Production builds -->
  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <DefineConstants>\$(DefineConstants);SANDBOX_TOOLS</DefineConstants>
  </PropertyGroup>
</Project>
EOF

cat << 'EOF' > Directory.Packages.props
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
EOF

cat << 'EOF' > nuget.config
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
EOF

cat << 'EOF' > .globalconfig
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
EOF

touch_files \
  ".editorconfig" \
  ".gitignore" \
  "README.md"

# ==============================================================================
# STEP 2: Documentation (docs/) & Database (db/) Trees
# ==============================================================================
echo "[2/9] Creating docs/ and db/ structure..."

touch_files \
  "docs/README.md" \
  "docs/architecture/c4-context.md" \
  "docs/architecture/c4-container.md" \
  "docs/architecture/c4-component-per-module.md" \
  "docs/architecture/module-dependency-map.md" \
  "docs/architecture/sequence-diagrams/payment-webhook-flow.md" \
  "docs/architecture/sequence-diagrams/outbox-fulfillment-flow.md" \
  "docs/architecture/sequence-diagrams/cash-advance-four-eyes-flow.md" \
  "docs/architecture/sequence-diagrams/audit-hash-chain-flow.md" \
  "docs/adr/0001-modular-monolith.md" \
  "docs/adr/0002-dapper-npgsql-over-efcore.md" \
  "docs/adr/0003-keep-postgres-stored-functions.md" \
  "docs/adr/0004-money-as-long-centavos.md" \
  "docs/adr/0005-transactional-outbox.md" \
  "docs/adr/0006-hybridcache-strategy.md" \
  "docs/adr/0007-authn-authz-model.md" \
  "docs/adr/0008-native-aot-vs-jit-readytorun.md" \
  "docs/adr/0009-strangler-fig-migration.md" \
  "docs/adr/0010-error-handling-result-pattern.md" \
  "docs/security/threat-model-stride.md" \
  "docs/security/owasp-asvs-checklist.md" \
  "docs/security/secrets-management.md" \
  "docs/security/sod-matrix.md" \
  "docs/runbooks/incident-webhook-backlog.md" \
  "docs/runbooks/incident-audit-chain-break.md" \
  "docs/runbooks/rollback-cutover.md" \
  "docs/runbooks/key-rotation.md" \
  "docs/api/openapi.v1.json" \
  "docs/api/endpoint-parity-matrix.md" \
  "docs/performance/latency-budgets.md" \
  "docs/performance/benchmark-baselines.md"

touch_files \
  "db/migrations/V0001__baseline_from_postgres_live_schema.sql" \
  "db/migrations/V0002__indexes_hot_paths.sql" \
  "db/migrations/V0003__audit_immutability_triggers.sql" \
  "db/migrations/V0004__outbox_lease_columns.sql" \
  "db/functions/fn_sanitize_input.sql" \
  "db/functions/fn_next_sequence_number.sql" \
  "db/functions/fn_append_audit_event.sql" \
  "db/functions/fn_authenticate_user.sql" \
  "db/functions/fn_oauth_state.sql" \
  "db/seed/approval_matrix.sql" \
  "db/seed/holiday_calendar_ph.sql" \
  "db/seed/tax_codes.sql" \
  "db/roles/app_runtime_role.sql" \
  "db/roles/app_migrator_role.sql" \
  "db/roles/app_readonly_role.sql"

# ==============================================================================
# STEP 3: Host (Composition Root)
# ==============================================================================
echo "[3/9] Scaffolding src/Host/Rge.Accounting.Host..."

HOST_DIR="src/Host/Rge.Accounting.Host"
mkdir -p "${HOST_DIR}"

cat << 'EOF' > "${HOST_DIR}/Rge.Accounting.Host.csproj"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <ProjectReference Include="..\..\BuildingBlocks\Rge.BuildingBlocks.Api\Rge.BuildingBlocks.Api.csproj" />
    <ProjectReference Include="..\..\BuildingBlocks\Rge.BuildingBlocks.Infrastructure\Rge.BuildingBlocks.Infrastructure.csproj" />
  </ItemGroup>
</Project>
EOF

touch_files \
  "${HOST_DIR}/Program.cs" \
  "${HOST_DIR}/appsettings.json" \
  "${HOST_DIR}/appsettings.Development.json" \
  "${HOST_DIR}/appsettings.Production.json" \
  "${HOST_DIR}/Composition/ModuleRegistry.cs" \
  "${HOST_DIR}/Composition/ServiceCollectionExtensions.cs" \
  "${HOST_DIR}/Composition/EndpointRouteBuilderExtensions.cs" \
  "${HOST_DIR}/Pipeline/CorrelationIdMiddleware.cs" \
  "${HOST_DIR}/Pipeline/SecurityHeadersMiddleware.cs" \
  "${HOST_DIR}/Pipeline/GlobalExceptionHandler.cs" \
  "${HOST_DIR}/Pipeline/RequestSizeLimitPolicy.cs" \
  "${HOST_DIR}/Pipeline/IdempotencyKeyMiddleware.cs" \
  "${HOST_DIR}/Pipeline/RequestLoggingEnricher.cs" \
  "${HOST_DIR}/Configuration/KestrelConfiguration.cs" \
  "${HOST_DIR}/Configuration/RateLimiterConfiguration.cs" \
  "${HOST_DIR}/Configuration/CorsConfiguration.cs" \
  "${HOST_DIR}/Configuration/OpenTelemetryConfiguration.cs" \
  "${HOST_DIR}/Configuration/SecretsConfiguration.cs" \
  "${HOST_DIR}/Properties/launchSettings.json"

# ==============================================================================
# STEP 4: BuildingBlocks
# ==============================================================================
echo "[4/9] Scaffolding src/BuildingBlocks..."

BB_DIR="src/BuildingBlocks"

# 4.1 Rge.BuildingBlocks.SharedKernel (Pure; zero dependencies)
mkdir -p "${BB_DIR}/Rge.BuildingBlocks.SharedKernel"
cat << 'EOF' > "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Rge.BuildingBlocks.SharedKernel.csproj"
<Project Sdk="Microsoft.NET.Sdk">
  <!-- Pure SharedKernel: Zero NuGet or external project dependencies allowed -->
</Project>
EOF

touch_files \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Primitives/Entity.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Primitives/AggregateRoot.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Primitives/ValueObject.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Primitives/DomainEvent.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Primitives/IClock.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Money/Money.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Money/Currency.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Money/VatCalculator.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Money/Rounding.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Results/Result.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Results/Error.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Results/ErrorType.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Time/PhilippineBusinessCalendar.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Time/IBusinessDayService.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.SharedKernel/Guards/Guard.cs"

# 4.2 Rge.BuildingBlocks.Application
mkdir -p "${BB_DIR}/Rge.BuildingBlocks.Application"
cat << 'EOF' > "${BB_DIR}/Rge.BuildingBlocks.Application/Rge.BuildingBlocks.Application.csproj"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Rge.BuildingBlocks.SharedKernel\Rge.BuildingBlocks.SharedKernel.csproj" />
  </ItemGroup>
</Project>
EOF

touch_files \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Abstractions/ICommand.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Abstractions/IQuery.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Abstractions/IHandler.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Abstractions/IUnitOfWork.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Abstractions/ICurrentActor.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Behaviors/ValidationBehavior.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Behaviors/AuditBehavior.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Behaviors/TransactionBehavior.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Behaviors/LoggingBehavior.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Behaviors/AuthorizationBehavior.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Behaviors/CachingBehavior.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Messaging/IIntegrationEvent.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Messaging/IOutboxWriter.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Pagination/KeysetPage.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Application/Pagination/PageRequest.cs"

# 4.3 Rge.BuildingBlocks.Infrastructure
mkdir -p "${BB_DIR}/Rge.BuildingBlocks.Infrastructure"
cat << 'EOF' > "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Rge.BuildingBlocks.Infrastructure.csproj"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Rge.BuildingBlocks.SharedKernel\Rge.BuildingBlocks.SharedKernel.csproj" />
    <ProjectReference Include="..\Rge.BuildingBlocks.Application\Rge.BuildingBlocks.Application.csproj" />
  </ItemGroup>
</Project>
EOF

touch_files \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Persistence/NpgsqlDataSourceFactory.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Persistence/ConnectionScope.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Persistence/DapperTypeHandlers.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Persistence/UnitOfWork.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Persistence/RetryPolicy.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Persistence/SqlTimeouts.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Outbox/OutboxStore.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Outbox/OutboxDispatcher.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Outbox/OutboxWorker.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Outbox/OutboxLeaseManager.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Caching/HybridCacheKeys.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Caching/CacheInvalidator.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Resilience/HttpResiliencePipelines.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Observability/ActivitySources.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Observability/Meters.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Observability/LogEnrichers.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Crypto/HashService.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Crypto/ConstantTimeComparer.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Crypto/HmacVerifier.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Infrastructure/Crypto/CanonicalJson.cs"

# 4.4 Rge.BuildingBlocks.Api
mkdir -p "${BB_DIR}/Rge.BuildingBlocks.Api"
cat << 'EOF' > "${BB_DIR}/Rge.BuildingBlocks.Api/Rge.BuildingBlocks.Api.csproj"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\Rge.BuildingBlocks.SharedKernel\Rge.BuildingBlocks.SharedKernel.csproj" />
    <ProjectReference Include="..\Rge.BuildingBlocks.Application\Rge.BuildingBlocks.Application.csproj" />
  </ItemGroup>
</Project>
EOF

touch_files \
  "${BB_DIR}/Rge.BuildingBlocks.Api/Endpoints/IEndpointModule.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Api/Endpoints/EndpointGroupExtensions.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Api/Filters/ValidationFilter.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Api/Filters/IdempotencyFilter.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Api/Http/ProblemDetailsFactory.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Api/Http/ResultToHttpMapper.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Api/Serialization/AppJsonContext.cs" \
  "${BB_DIR}/Rge.BuildingBlocks.Api/Serialization/JsonDefaults.cs"

# ==============================================================================
# STEP 5: Modules (8 Business Modules)
# ==============================================================================
echo "[5/9] Scaffolding src/Modules (Payments, Receipts, Finance, Identity, Audit, CostAllocation, Documents, Integrations)..."

MODULES=("Payments" "Receipts" "Finance" "Identity" "Audit" "CostAllocation" "Documents" "Integrations")

for mod in "${MODULES[@]}"; do
  create_contracts_csproj "src/Modules/${mod}/Rge.${mod}.Contracts/Rge.${mod}.Contracts.csproj"
  create_domain_csproj    "src/Modules/${mod}/Rge.${mod}.Domain/Rge.${mod}.Domain.csproj"
  create_app_csproj       "${mod}"
  create_infra_csproj     "${mod}"
  create_api_csproj       "${mod}"
done

# ------------------------------------------------------------------------------
# 5.1 Module: Payments (The Hot Path)
# ------------------------------------------------------------------------------
PAY_DIR="src/Modules/Payments"

touch_files \
  "${PAY_DIR}/Rge.Payments.Contracts/Events/OrderPaidIntegrationEvent.cs" \
  "${PAY_DIR}/Rge.Payments.Contracts/Events/PaymentFailedIntegrationEvent.cs" \
  "${PAY_DIR}/Rge.Payments.Contracts/Abstractions/IOrderLookup.cs" \
  "${PAY_DIR}/Rge.Payments.Contracts/Abstractions/IPaymentStatusQuery.cs"

touch_files \
  "${PAY_DIR}/Rge.Payments.Domain/Orders/Order.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Orders/OrderStatus.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Orders/OrderCategory.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Orders/OrderNumber.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Orders/OrderLineItem.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Orders/Events/OrderCreated.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Orders/Events/OrderPaid.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Orders/Events/OrderCancelled.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Orders/Events/OrderRefunded.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/PaymentAttempts/PaymentAttempt.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/PaymentAttempts/PaymentAttemptStatus.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/PaymentAttempts/PaymentAttemptStateMachine.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/PaymentAttempts/PaymentMethod.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/PaymentAttempts/IdempotencyKey.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/PaymentAttempts/FailureCode.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Webhooks/WebhookEvent.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Webhooks/WebhookProcessingStatus.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Webhooks/WebhookEventKey.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Pricing/IPricingPolicy.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Pricing/ServerAuthoritativePricing.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Errors/PaymentErrors.cs" \
  "${PAY_DIR}/Rge.Payments.Domain/Errors/OrderErrors.cs"

touch_files \
  "${PAY_DIR}/Rge.Payments.Application/Orders/CreateOrder/CreateOrderCommand.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Orders/CreateOrder/CreateOrderHandler.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Orders/CreateOrder/CreateOrderValidator.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Orders/CreateOrder/CreateOrderResponse.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Orders/GetOrder/GetOrderQuery.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Orders/GetOrder/GetOrderHandler.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Orders/GetOrder/GetOrderResponse.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Orders/ListOrders/ListOrdersQuery.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Orders/ListOrders/ListOrdersHandler.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Orders/ListOrders/ListOrdersResponse.cs" \
  "${PAY_DIR}/Rge.Payments.Application/PaymentAttempts/InitiateAttempt/InitiateAttemptCommand.cs" \
  "${PAY_DIR}/Rge.Payments.Application/PaymentAttempts/InitiateAttempt/InitiateAttemptHandler.cs" \
  "${PAY_DIR}/Rge.Payments.Application/PaymentAttempts/InitiateAttempt/InitiateAttemptValidator.cs" \
  "${PAY_DIR}/Rge.Payments.Application/PaymentAttempts/PollAndReconcile/PollAndReconcileQuery.cs" \
  "${PAY_DIR}/Rge.Payments.Application/PaymentAttempts/PollAndReconcile/PollAndReconcileHandler.cs" \
  "${PAY_DIR}/Rge.Payments.Application/PaymentAttempts/ExpireAttempt/ExpireAttemptCommand.cs" \
  "${PAY_DIR}/Rge.Payments.Application/PaymentAttempts/ExpireAttempt/ExpireAttemptHandler.cs" \
  "${PAY_DIR}/Rge.Payments.Application/PaymentAttempts/FailAttempt/FailAttemptCommand.cs" \
  "${PAY_DIR}/Rge.Payments.Application/PaymentAttempts/FailAttempt/FailAttemptHandler.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Webhooks/ReceivePayMongoWebhook/ReceivePayMongoWebhookCommand.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Webhooks/ReceivePayMongoWebhook/ReceivePayMongoWebhookHandler.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Webhooks/ProcessWebhookEvent/ProcessWebhookEventHandler.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Reconciliation/ReconcileOne/ReconcileOneCommand.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Reconciliation/ReconcileOne/ReconcileOneHandler.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Reconciliation/ReconcileAllNonTerminal/ReconcileAllNonTerminalCommand.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Reconciliation/ReconcileAllNonTerminal/ReconcileAllNonTerminalHandler.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Sandbox/SimulateWebhook/SimulateWebhookCommand.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Sandbox/SimulateWebhook/SimulateWebhookHandler.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Sandbox/CreateAndExpireTest/CreateAndExpireTestCommand.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Sandbox/CreateAndExpireTest/CreateAndExpireTestHandler.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Ports/IPaymentGateway.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Ports/IOrderRepository.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Ports/IPaymentAttemptRepository.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Ports/IWebhookEventStore.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Ports/IProductCatalog.cs" \
  "${PAY_DIR}/Rge.Payments.Application/Mapping/PaymentMappings.cs"

touch_files \
  "${PAY_DIR}/Rge.Payments.Infrastructure/Persistence/Repositories/OrderRepository.cs" \
  "${PAY_DIR}/Rge.Payments.Infrastructure/Persistence/Repositories/PaymentAttemptRepository.cs" \
  "${PAY_DIR}/Rge.Payments.Infrastructure/Persistence/Repositories/WebhookEventStore.cs" \
  "${PAY_DIR}/Rge.Payments.Infrastructure/Persistence/Sql/OrderQueries.cs" \
  "${PAY_DIR}/Rge.Payments.Infrastructure/Persistence/Sql/AttemptQueries.cs" \
  "${PAY_DIR}/Rge.Payments.Infrastructure/Persistence/Mappings/OrderMaterializer.cs" \
  "${PAY_DIR}/Rge.Payments.Infrastructure/Persistence/Mappings/PaymentAttemptMaterializer.cs" \
  "${PAY_DIR}/Rge.Payments.Infrastructure/PayMongo/PayMongoClient.cs" \
  "${PAY_DIR}/Rge.Payments.Infrastructure/PayMongo/PayMongoSignatureVerifier.cs" \
  "${PAY_DIR}/Rge.Payments.Infrastructure/PayMongo/PayMongoDtos.cs" \
  "${PAY_DIR}/Rge.Payments.Infrastructure/PayMongo/PayMongoOptions.cs" \
  "${PAY_DIR}/Rge.Payments.Infrastructure/Catalog/JsonProductCatalog.cs" \
  "${PAY_DIR}/Rge.Payments.Infrastructure/Outbox/OrderPaidOutboxPublisher.cs" \
  "${PAY_DIR}/Rge.Payments.Infrastructure/DependencyInjection.cs"

touch_files \
  "${PAY_DIR}/Rge.Payments.Api/PaymentsEndpoints.cs" \
  "${PAY_DIR}/Rge.Payments.Api/Orders/CreateOrderEndpoint.cs" \
  "${PAY_DIR}/Rge.Payments.Api/Orders/GetOrderEndpoint.cs" \
  "${PAY_DIR}/Rge.Payments.Api/Orders/ListOrdersEndpoint.cs" \
  "${PAY_DIR}/Rge.Payments.Api/Attempts/InitiateAttemptEndpoint.cs" \
  "${PAY_DIR}/Rge.Payments.Api/Attempts/GetPaymentEndpoint.cs" \
  "${PAY_DIR}/Rge.Payments.Api/Attempts/ExpireEndpoint.cs" \
  "${PAY_DIR}/Rge.Payments.Api/Attempts/FailEndpoint.cs" \
  "${PAY_DIR}/Rge.Payments.Api/Webhooks/PayMongoWebhookEndpoint.cs" \
  "${PAY_DIR}/Rge.Payments.Api/Admin/ReconcileEndpoints.cs" \
  "${PAY_DIR}/Rge.Payments.Api/Admin/WebhookEventsEndpoint.cs" \
  "${PAY_DIR}/Rge.Payments.Api/Admin/OutboxEndpoints.cs" \
  "${PAY_DIR}/Rge.Payments.Api/Sandbox/SandboxEndpoints.cs" \
  "${PAY_DIR}/Rge.Payments.Api/Contracts/PaymentsContracts.cs"

# ------------------------------------------------------------------------------
# 5.2 Module: Receipts (BIR Compliance)
# ------------------------------------------------------------------------------
REC_DIR="src/Modules/Receipts"

touch_files \
  "${REC_DIR}/Rge.Receipts.Domain/OfficialReceipt.cs" \
  "${REC_DIR}/Rge.Receipts.Domain/OfficialReceiptNumber.cs" \
  "${REC_DIR}/Rge.Receipts.Domain/TreasuryAcknowledgement.cs" \
  "${REC_DIR}/Rge.Receipts.Domain/VatBreakdown.cs" \
  "${REC_DIR}/Rge.Receipts.Domain/AuditDigest.cs" \
  "${REC_DIR}/Rge.Receipts.Domain/Policies/BirRmc77_2024Policy.cs" \
  "${REC_DIR}/Rge.Receipts.Domain/Policies/RoundingPolicy.cs"

touch_files \
  "${REC_DIR}/Rge.Receipts.Application/IssueReceipt/IssueReceiptHandler.cs" \
  "${REC_DIR}/Rge.Receipts.Application/GetReceiptByPayment/GetReceiptByPaymentQuery.cs" \
  "${REC_DIR}/Rge.Receipts.Application/GetReceiptByPayment/GetReceiptByPaymentHandler.cs" \
  "${REC_DIR}/Rge.Receipts.Application/GetReceiptByOrder/GetReceiptByOrderQuery.cs" \
  "${REC_DIR}/Rge.Receipts.Application/GetReceiptByOrder/GetReceiptByOrderHandler.cs" \
  "${REC_DIR}/Rge.Receipts.Application/RenderReceiptPdf/RenderReceiptPdfQuery.cs" \
  "${REC_DIR}/Rge.Receipts.Application/RenderReceiptPdf/RenderReceiptPdfHandler.cs" \
  "${REC_DIR}/Rge.Receipts.Application/ResendReceiptEmail/ResendReceiptEmailCommand.cs" \
  "${REC_DIR}/Rge.Receipts.Application/ResendReceiptEmail/ResendReceiptEmailHandler.cs" \
  "${REC_DIR}/Rge.Receipts.Application/Ports/IReceiptRepository.cs" \
  "${REC_DIR}/Rge.Receipts.Application/Ports/IPdfRenderer.cs" \
  "${REC_DIR}/Rge.Receipts.Application/Ports/IReceiptMailer.cs" \
  "${REC_DIR}/Rge.Receipts.Application/Ports/ICanonicalJsonSerializer.cs"

touch_files \
  "${REC_DIR}/Rge.Receipts.Infrastructure/Persistence/ReceiptRepository.cs" \
  "${REC_DIR}/Rge.Receipts.Infrastructure/Persistence/Sql/ReceiptQueries.cs" \
  "${REC_DIR}/Rge.Receipts.Infrastructure/Pdf/QuestPdfReceiptRenderer.cs" \
  "${REC_DIR}/Rge.Receipts.Infrastructure/Pdf/ReceiptLayout.cs" \
  "${REC_DIR}/Rge.Receipts.Infrastructure/Pdf/FontAssets/.gitkeep" \
  "${REC_DIR}/Rge.Receipts.Infrastructure/Mail/SmtpReceiptMailer.cs" \
  "${REC_DIR}/Rge.Receipts.Infrastructure/Mail/EmailTemplates/.gitkeep" \
  "${REC_DIR}/Rge.Receipts.Infrastructure/Digest/CanonicalJsonSerializer.cs" \
  "${REC_DIR}/Rge.Receipts.Infrastructure/Digest/DigestService.cs" \
  "${REC_DIR}/Rge.Receipts.Infrastructure/DependencyInjection.cs"

touch_files \
  "${REC_DIR}/Rge.Receipts.Api/ReceiptEndpoints.cs" \
  "${REC_DIR}/Rge.Receipts.Api/Contracts/ReceiptContracts.cs"

# ------------------------------------------------------------------------------
# 5.3 Module: Finance (Advances, Disbursements, Liquidation, Policy, P2P)
# ------------------------------------------------------------------------------
FIN_DIR="src/Modules/Finance"

touch_files \
  "${FIN_DIR}/Rge.Finance.Contracts/IAdvanceBalanceQuery.cs" \
  "${FIN_DIR}/Rge.Finance.Contracts/ISodGuard.cs" \
  "${FIN_DIR}/Rge.Finance.Contracts/AdvanceClosedIntegrationEvent.cs"

touch_files \
  "${FIN_DIR}/Rge.Finance.Domain/CashAdvances/CashAdvance.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/CashAdvances/StateMachines/ApprovalStatus.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/CashAdvances/StateMachines/PaymentStatus.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/CashAdvances/StateMachines/LiquidationReviewStatus.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/CashAdvances/StateMachines/SettlementStatus.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/CashAdvances/StateMachines/PostingStatus.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/CashAdvances/StateMachines/StateTransitionTables.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/CashAdvances/AdvanceNumber.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/CashAdvances/PurposeCode.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/CashAdvances/Events/CashAdvanceEvents.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Disbursements/Disbursement.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Disbursements/DisbursementStage.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Disbursements/FourEyesRule.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Disbursements/BankReference.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Ledger/AdvanceLedgerEntry.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Ledger/LedgerEntryType.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Ledger/AdvanceBalanceCalculator.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Liquidation/ExpenseReport.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Liquidation/ExpenseLine.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Liquidation/DuplicateFingerprint.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Liquidation/SettlementApplication.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Liquidation/ClosingSnapshot.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Policy/FinancialPolicy.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Policy/PolicyVersion.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Policy/PolicyRule.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Policy/PeriodCounter.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Policy/ExceptionRequest.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Policy/PolicyEvaluator.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Procurement/PurchaseRequest.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Procurement/PurchaseRequestLine.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Procurement/AccountPayable.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Procurement/NormalizedInvoiceNumber.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Procurement/ThreeWayMatch.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Procurement/PaymentRecord.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Vendors/Vendor.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Vendors/VendorBankChange.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Vendors/MaskedBankAccount.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Vendors/TaxCode.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Exceptions/ExceptionRegisterItem.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Approvals/ApprovalMatrix.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Approvals/ApprovalHistoryEntry.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Approvals/ApprovalRouter.cs" \
  "${FIN_DIR}/Rge.Finance.Domain/Errors/FinanceErrors.cs"

create_vertical_slices "${FIN_DIR}/Rge.Finance.Application/CashAdvances" \
  Create Submit Decide GetById List CashReturn ReimbursementSettle Close
create_vertical_slices "${FIN_DIR}/Rge.Finance.Application/Disbursements" \
  Prepare Verify BankApprove Execute VerifyEvidence SetExceptionState
create_vertical_slices "${FIN_DIR}/Rge.Finance.Application/ExpenseReports" \
  Create Patch AddLines Evaluate Submit Review GetById List
create_vertical_slices "${FIN_DIR}/Rge.Finance.Application/Policies" \
  AuthorVersion SubmitVersion ApproveVersion RetireVersion List
create_vertical_slices "${FIN_DIR}/Rge.Finance.Application/ExceptionRequests" \
  Submit Decide List
create_vertical_slices "${FIN_DIR}/Rge.Finance.Application/Vendors" \
  Register Verify RequestBankChange DecideBankChange List
create_vertical_slices "${FIN_DIR}/Rge.Finance.Application/TaxCodes" \
  Register List
create_vertical_slices "${FIN_DIR}/Rge.Finance.Application/PurchaseRequests" \
  Create Submit Decide GetById List
create_vertical_slices "${FIN_DIR}/Rge.Finance.Application/Payables" \
  Register PreparePayment VerifyPayment BankApprovePayment ExecutePayment ReconcilePayment SetPaymentExceptionState
create_vertical_slices "${FIN_DIR}/Rge.Finance.Application/Reporting" \
  DashboardSummary KpiDefinitions BusinessConfirmations Tracker TrackerCsvExport AgingBuckets ExceptionRegister

touch_files \
  "${FIN_DIR}/Rge.Finance.Application/Security/SodGuard.cs" \
  "${FIN_DIR}/Rge.Finance.Application/Security/ApprovalLimitGuard.cs" \
  "${FIN_DIR}/Rge.Finance.Application/Security/ScopeGuard.cs" \
  "${FIN_DIR}/Rge.Finance.Application/Ports/IFinanceRepositories.cs"

touch_files \
  "${FIN_DIR}/Rge.Finance.Infrastructure/Persistence/Repositories/.gitkeep" \
  "${FIN_DIR}/Rge.Finance.Infrastructure/Persistence/Sql/.gitkeep" \
  "${FIN_DIR}/Rge.Finance.Infrastructure/Persistence/ReadModels/.gitkeep" \
  "${FIN_DIR}/Rge.Finance.Infrastructure/Reporting/DashboardReadService.cs" \
  "${FIN_DIR}/Rge.Finance.Infrastructure/Reporting/TrackerReadService.cs" \
  "${FIN_DIR}/Rge.Finance.Infrastructure/Reporting/CsvInjectionSafeWriter.cs" \
  "${FIN_DIR}/Rge.Finance.Infrastructure/Policy/PolicyCache.cs" \
  "${FIN_DIR}/Rge.Finance.Infrastructure/Policy/PolicyCacheInvalidator.cs" \
  "${FIN_DIR}/Rge.Finance.Infrastructure/Calendar/PhHolidayCalendarProvider.cs" \
  "${FIN_DIR}/Rge.Finance.Infrastructure/DependencyInjection.cs"

touch_files \
  "${FIN_DIR}/Rge.Finance.Api/FinanceEndpoints.cs" \
  "${FIN_DIR}/Rge.Finance.Api/CashAdvances/CashAdvanceEndpoints.cs" \
  "${FIN_DIR}/Rge.Finance.Api/Disbursements/DisbursementEndpoints.cs" \
  "${FIN_DIR}/Rge.Finance.Api/ExpenseReports/ExpenseReportEndpoints.cs" \
  "${FIN_DIR}/Rge.Finance.Api/Policies/PolicyEndpoints.cs" \
  "${FIN_DIR}/Rge.Finance.Api/Exceptions/ExceptionEndpoints.cs" \
  "${FIN_DIR}/Rge.Finance.Api/Vendors/VendorEndpoints.cs" \
  "${FIN_DIR}/Rge.Finance.Api/Procurement/ProcurementEndpoints.cs" \
  "${FIN_DIR}/Rge.Finance.Api/Payables/PayableEndpoints.cs" \
  "${FIN_DIR}/Rge.Finance.Api/Reporting/ReportingEndpoints.cs" \
  "${FIN_DIR}/Rge.Finance.Api/Contracts/FinanceContracts.cs"

# ------------------------------------------------------------------------------
# 5.4 Module: Identity
# ------------------------------------------------------------------------------
ID_DIR="src/Modules/Identity"

touch_files \
  "${ID_DIR}/Rge.Identity.Domain/Actor.cs" \
  "${ID_DIR}/Rge.Identity.Domain/Role.cs" \
  "${ID_DIR}/Rge.Identity.Domain/RoleAssignment.cs" \
  "${ID_DIR}/Rge.Identity.Domain/Session.cs" \
  "${ID_DIR}/Rge.Identity.Domain/OAuthState.cs" \
  "${ID_DIR}/Rge.Identity.Domain/LockoutPolicy.cs"

create_vertical_slices "${ID_DIR}/Rge.Identity.Application" \
  Login Logout GetSession CreateCsrfToken OAuthBegin OAuthCallback ResolveActor ListFinanceUsers SwitchUser ActAs

touch_files \
  "${ID_DIR}/Rge.Identity.Application/Ports/IIdentityPorts.cs" \
  "${ID_DIR}/Rge.Identity.Infrastructure/PgAuthenticator.cs" \
  "${ID_DIR}/Rge.Identity.Infrastructure/PgOAuthStateStore.cs" \
  "${ID_DIR}/Rge.Identity.Infrastructure/SessionTokenHasher.cs" \
  "${ID_DIR}/Rge.Identity.Infrastructure/CsrfTokenService.cs" \
  "${ID_DIR}/Rge.Identity.Infrastructure/ActorCache.cs" \
  "${ID_DIR}/Rge.Identity.Infrastructure/RoleStore.cs" \
  "${ID_DIR}/Rge.Identity.Infrastructure/DependencyInjection.cs" \
  "${ID_DIR}/Rge.Identity.Api/AuthEndpoints.cs" \
  "${ID_DIR}/Rge.Identity.Api/FinanceIdentityEndpoints.cs" \
  "${ID_DIR}/Rge.Identity.Api/Authentication/SessionAuthHandler.cs" \
  "${ID_DIR}/Rge.Identity.Api/Authentication/BearerAuthHandler.cs" \
  "${ID_DIR}/Rge.Identity.Api/Authorization/PermissionRequirement.cs" \
  "${ID_DIR}/Rge.Identity.Api/Authorization/SodRequirement.cs" \
  "${ID_DIR}/Rge.Identity.Api/Authorization/ScopeRequirement.cs" \
  "${ID_DIR}/Rge.Identity.Api/Authorization/Handlers/AuthorizationHandlers.cs" \
  "${ID_DIR}/Rge.Identity.Api/Authorization/PolicyNames.cs" \
  "${ID_DIR}/Rge.Identity.Api/Authorization/PermissionCatalog.cs"

# ------------------------------------------------------------------------------
# 5.5 Module: Audit
# ------------------------------------------------------------------------------
AUD_DIR="src/Modules/Audit"

touch_files \
  "${AUD_DIR}/Rge.Audit.Contracts/IAuditTrail.cs" \
  "${AUD_DIR}/Rge.Audit.Domain/AuditEvent.cs" \
  "${AUD_DIR}/Rge.Audit.Domain/AuditHash.cs" \
  "${AUD_DIR}/Rge.Audit.Domain/AuditAction.cs"

create_vertical_slices "${AUD_DIR}/Rge.Audit.Application" \
  AppendAuditEvent QueryAuditEvents VerifyChain

touch_files \
  "${AUD_DIR}/Rge.Audit.Infrastructure/PgAuditAppender.cs" \
  "${AUD_DIR}/Rge.Audit.Infrastructure/ChainVerifier.cs" \
  "${AUD_DIR}/Rge.Audit.Infrastructure/AuditQueryReader.cs" \
  "${AUD_DIR}/Rge.Audit.Infrastructure/DependencyInjection.cs" \
  "${AUD_DIR}/Rge.Audit.Api/AuditEndpoints.cs"

# ------------------------------------------------------------------------------
# 5.6 Module: CostAllocation
# ------------------------------------------------------------------------------
COST_DIR="src/Modules/CostAllocation"

touch_files \
  "${COST_DIR}/Rge.CostAllocation.Domain/CostDepartment.cs" \
  "${COST_DIR}/Rge.CostAllocation.Domain/CostProduct.cs" \
  "${COST_DIR}/Rge.CostAllocation.Domain/CostActivity.cs" \
  "${COST_DIR}/Rge.CostAllocation.Domain/CostDriver.cs" \
  "${COST_DIR}/Rge.CostAllocation.Domain/BudgetCap.cs" \
  "${COST_DIR}/Rge.CostAllocation.Domain/AllocationCalculator.cs"

create_vertical_slices "${COST_DIR}/Rge.CostAllocation.Application" \
  GetCostAllocation UpdateDepartmentCost

touch_files \
  "${COST_DIR}/Rge.CostAllocation.Application/Ports/ICostAllocationPorts.cs" \
  "${COST_DIR}/Rge.CostAllocation.Infrastructure/JsonConfigurationStore.cs" \
  "${COST_DIR}/Rge.CostAllocation.Infrastructure/LegacyDashboardReader.cs" \
  "${COST_DIR}/Rge.CostAllocation.Infrastructure/DependencyInjection.cs" \
  "${COST_DIR}/Rge.CostAllocation.Api/CostAllocationEndpoints.cs" \
  "${COST_DIR}/Rge.CostAllocation.Api/LegacyDashboardEndpoints.cs" \
  "${COST_DIR}/Rge.CostAllocation.Api/ConfigurationEndpoints.cs"

# ------------------------------------------------------------------------------
# 5.7 Module: Documents
# ------------------------------------------------------------------------------
DOC_DIR="src/Modules/Documents"

touch_files \
  "${DOC_DIR}/Rge.Documents.Domain/DocumentReference.cs" \
  "${DOC_DIR}/Rge.Documents.Domain/MimeSignature.cs" \
  "${DOC_DIR}/Rge.Documents.Domain/FileSizePolicy.cs"

create_vertical_slices "${DOC_DIR}/Rge.Documents.Application" \
  UploadDocument ListDocuments DownloadDocument

touch_files \
  "${DOC_DIR}/Rge.Documents.Application/Ports/IDocumentPorts.cs" \
  "${DOC_DIR}/Rge.Documents.Infrastructure/MagicByteValidator.cs" \
  "${DOC_DIR}/Rge.Documents.Infrastructure/Sha256Hasher.cs" \
  "${DOC_DIR}/Rge.Documents.Infrastructure/SafeFileStore.cs" \
  "${DOC_DIR}/Rge.Documents.Infrastructure/AntivirusScanHook.cs" \
  "${DOC_DIR}/Rge.Documents.Infrastructure/PathTraversalGuard.cs" \
  "${DOC_DIR}/Rge.Documents.Infrastructure/DependencyInjection.cs" \
  "${DOC_DIR}/Rge.Documents.Api/DocumentEndpoints.cs"

# ------------------------------------------------------------------------------
# 5.8 Module: Integrations
# ------------------------------------------------------------------------------
INT_DIR="src/Modules/Integrations"

touch_files \
  "${INT_DIR}/Rge.Integrations.Domain/GoogleFormSubmission.cs" \
  "${INT_DIR}/Rge.Integrations.Domain/SheetSchema18.cs" \
  "${INT_DIR}/Rge.Integrations.Domain/HeaderAliasMap.cs"

create_vertical_slices "${INT_DIR}/Rge.Integrations.Application" \
  SubmitGoogleForm GetFormStatus IntegrationStatusProbe TestPayMongo TestSmtp SaveCredentials SheetSyncHandler

touch_files \
  "${INT_DIR}/Rge.Integrations.Infrastructure/GoogleSheetsClient.cs" \
  "${INT_DIR}/Rge.Integrations.Infrastructure/SheetRowMapper.cs" \
  "${INT_DIR}/Rge.Integrations.Infrastructure/SmtpClientFactory.cs" \
  "${INT_DIR}/Rge.Integrations.Infrastructure/FormSecretVerifier.cs" \
  "${INT_DIR}/Rge.Integrations.Infrastructure/SecretStoreWriter.cs" \
  "${INT_DIR}/Rge.Integrations.Infrastructure/DependencyInjection.cs" \
  "${INT_DIR}/Rge.Integrations.Api/GoogleFormsEndpoints.cs" \
  "${INT_DIR}/Rge.Integrations.Api/IntegrationEndpoints.cs"

# ==============================================================================
# STEP 6: Tests (Cross-Cutting & Module Test Suites)
# ==============================================================================
echo "[6/9] Scaffolding tests/..."

CROSS_TESTS=(
  "Architecture.Tests"
  "Contract.Parity.Tests"
  "Security.Tests"
  "Performance.Tests"
  "Integration.Tests"
  "Tests.Common"
)

for t in "${CROSS_TESTS[@]}"; do
  create_test_csproj "tests/${t}/Rge.${t}.csproj"
done

MODULE_TESTS=(
  "Payments.Domain.Tests"
  "Payments.Application.Tests"
  "Payments.Integration.Tests"
  "Receipts.Domain.Tests"
  "Receipts.Application.Tests"
  "Receipts.Integration.Tests"
  "Finance.Domain.Tests"
  "Finance.Application.Tests"
  "Finance.Integration.Tests"
  "Identity.Domain.Tests"
  "Identity.Application.Tests"
  "Identity.Integration.Tests"
  "Audit.Domain.Tests"
  "Audit.Integration.Tests"
  "CostAllocation.Domain.Tests"
  "CostAllocation.Application.Tests"
  "Documents.Domain.Tests"
  "Documents.Integration.Tests"
  "Integrations.Application.Tests"
  "Integrations.Integration.Tests"
)

for mt in "${MODULE_TESTS[@]}"; do
  create_test_csproj "tests/Modules/${mt}/Rge.${mt}.csproj"
done

# ==============================================================================
# STEP 7: Migration & Verification Tools (tools/)
# ==============================================================================
echo "[7/9] Scaffolding tools/..."

TOOLS=(
  "GoldenMaster.Exporter"
  "ParityRunner"
  "DataMigration.Verifier"
  "AuditChain.Verifier"
)

for tool in "${TOOLS[@]}"; do
  create_tool_csproj "tools/${tool}/Rge.${tool}.csproj"
  touch_files "tools/${tool}/Program.cs"
done

# ==============================================================================
# STEP 8: Deploy & GitHub Actions Workflows
# ==============================================================================
echo "[8/9] Scaffolding deploy/ and .github/workflows/..."

touch_files \
  "deploy/docker/Dockerfile" \
  "deploy/docker/Dockerfile.migrator" \
  "deploy/docker/docker-compose.yml" \
  "deploy/docker/docker-compose.test.yml" \
  "deploy/k8s/deployment.yaml" \
  "deploy/k8s/hpa.yaml" \
  "deploy/k8s/networkpolicy.yaml" \
  "deploy/k8s/pdb.yaml" \
  "deploy/k8s/secrets-provider-class.yaml" \
  "deploy/proxy/yarp-strangler-routes.json" \
  "deploy/proxy/nginx-strangler.conf" \
  "deploy/observability/otel-collector.yaml" \
  "deploy/observability/grafana-dashboards.json" \
  "deploy/observability/alert-rules.yaml"

touch_files \
  ".github/workflows/ci-build-test.yml" \
  ".github/workflows/ci-security-scan.yml" \
  ".github/workflows/ci-parity.yml" \
  ".github/workflows/ci-performance-gate.yml" \
  ".github/workflows/cd-release.yml"

# ==============================================================================
# STEP 9: Generate Rge.Accounting.slnx (.NET 10 XML Solution File)
# ==============================================================================
echo "[9/9] Generating ${SOLUTION_NAME}.slnx..."

cat << 'EOF' > "${SOLUTION_NAME}.slnx"
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
EOF

echo "=============================================================================="
echo " Scaffold complete!"
echo " Root location : $(pwd)"
echo " Solution file : $(pwd)/${SOLUTION_NAME}.slnx"
echo "=============================================================================="