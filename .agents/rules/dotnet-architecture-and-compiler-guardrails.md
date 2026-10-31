---
description: .NET 10 Modular Monolith architectural rules, Roslyn compiler guardrails, and namespace hygiene
---

# .NET 10 Architecture & Compiler Guardrails

## 1. Single-Node Deterministic Builds in Windows Sandbox
- Always append `-m:1 --disable-build-servers` to all `dotnet build`, `dotnet restore`, and `dotnet test` commands run in sandbox environments.
- Multi-process worker IPC and persistent build servers fail intermittently with `MSB4276` or child worker node communication timeouts under Windows sandbox environments.

## 2. Namespace Collision Prevention (`SecurityGuards` vs `Security`)
- **Invariant**: Never name an application or domain folder `Security` within projects that reference BCL namespaces containing `Security` (such as `System.Security.Claims`, `System.Security.Cryptography`).
- **Rationale**: The C# Roslyn compiler resolves unadorned `Security` tokens against the local project namespace rather than `System.Security`, producing `CS0234` or `CS0246` type resolution failures.
- **Pattern**: Name security application folders `SecurityGuards` (e.g., `Rge.Finance.Application/SecurityGuards/`).

## 3. Roslyn CA1819 Array Property Suppression on DTOs and Commands
- **Invariant**: Under `TreatWarningsAsErrors=true` and `latest-all` Roslyn analyzers, properties returning arrays trigger `CA1819: Properties should not return arrays`.
- **Pattern**: When binary payloads (e.g., `byte[] FileBytes`, `byte[] Content`) are required in commands or DTOs, annotate the type or member with:
  ```csharp
  [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Binary payload transfer")]
  ```

## 4. ASP.NET Core Framework References in Infrastructure Projects
- Any library or infrastructure project implementing dependency injection extensions (`IServiceCollection`) or hosting middleware must explicitly include:
  ```xml
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
  ```
  in its `.csproj` file to resolve ASP.NET Core abstractions without pulling mismatched NuGet packages.

## 5. ASP.NET Core Minimal API Authorization Middleware Pipeline Invariant
- **Invariant**: If any mapped route contains authorization metadata (via `.RequireAuthorization()` or `[Authorize]`), `app.UseAuthentication()` and `app.UseAuthorization()` MUST be invoked in `Program.cs` before endpoint routing.
- **DI Registration**: `services.AddAuthentication(...)` and `services.AddAuthorizationBuilder()` must be configured in `IServiceCollection` with default challenge schemes and named policies (e.g., `Policy.SysAdmin`).
- **Failure Mode**: Omitting `app.UseAuthorization()` causes Kestrel's `EndpointMiddleware` to throw `System.InvalidOperationException: Endpoint ... contains authorization metadata, but a middleware was not found that supports authorization`, returning 500 Internal Server Error.

## 6. Public Checkout vs Internal Protected API Boundary
- **Invariant**: Public customer checkout routes (`/api/v1/orders`, `/api/v1/orders/{id}/payment-attempts`, `/api/v1/payments/{id}`) and developer sandbox inspection endpoints (`/api/v1/sandbox/*`, `/api/v1/admin/fulfillment-outbox`, `/api/v1/admin/webhook-events`) MUST NOT be decorated with `.RequireAuthorization()`.
- **Rationale**: Next.js client components and guest checkout server actions invoke these endpoints without corporate bearer tokens or employee cookies. Restricting them breaks legacy contract parity and prevents customers or dashboard tabs from loading data.
- **Pattern**: Enforce authorization strictly on internal corporate finance workflows (cash advances, disbursements, reconciliations) using `ICurrentActor` and role verification.

## 7. Dynamic PaaS & Preview Domain Ingestion in Strict CORS
- **Invariant**: Kestrel's CORS policy must never rely solely on hardcoded localhost addresses.
- **Pattern**: In `CorsConfiguration.cs`, dynamically read environment variables (`FRONTEND_ORIGIN`, `NEXT_PUBLIC_APP_DOMAIN`, `APP_DOMAIN`), strip any trailing slashes, and add them to the allowed origins set alongside localhost and production domains.

## 8. Direct Project References for Core SharedKernel Types
- **Invariant**: Any module or infrastructure project consuming core domain primitives (`Money`, `IClock`, `Result`) must declare a direct `<ProjectReference Include="..\..\..\BuildingBlocks\Rge.BuildingBlocks.SharedKernel\Rge.BuildingBlocks.SharedKernel.csproj" />`.
- **Rationale**: Relying purely on transitive project dependencies can cause CLR assembly load failures (`FileNotFoundException`) when projects are built or versioned independently under MinVer.

## 9. Minimal API JSON Property Serialization Collision Invariant
- **Invariant**: When returning anonymous types or records from endpoints with `JsonNamingPolicy.SnakeCaseLower`, **never manually project both camelCase and snake_case properties** for the same data element (e.g. `costBreakdown` and `cost_breakdown`).
- **Rationale**: The serializer's naming policy converts `costBreakdown` to `cost_breakdown`, colliding with the existing property key and throwing `System.InvalidOperationException: The JSON property name for '<>f__AnonymousType...cost_breakdown' collides with another property` (500 Internal Server Error).
- **Pattern**: Let the serializer's `PropertyNamingPolicy` handle the case transformation, or use explicit snake_case naming exclusively across all properties in the anonymous object.

## 10. Session Token Cookie Authentication Scheme Invariant
- **Invariant**: In platforms where sessions are hashed and validated via a custom database or cache store (e.g. `rge_session`), `SessionAndBearerAuthHandler` (or equivalent custom token scheme) MUST be registered as `options.DefaultScheme` and `options.DefaultChallengeScheme`.
- **Rationale**: Registering `AddCookie()` sets the default handler to ASP.NET Core Data Protection cookies, which expects encrypted tickets and fails with `Cookies was not authenticated. Failure message: Unprotect ticket failed` when receiving platform session tokens.
- **Pattern**:
  ```csharp
  services.AddAuthentication(options =>
  {
      options.DefaultScheme = SessionAndBearerAuthHandler.SchemeName;
      options.DefaultChallengeScheme = SessionAndBearerAuthHandler.SchemeName;
  })
  .AddScheme<AuthenticationSchemeOptions, SessionAndBearerAuthHandler>(
      SessionAndBearerAuthHandler.SchemeName, _ => { });
  ```

## 11. Dual-Casing Request Model Binding Invariant (camelCase & snake_case)
- **Invariant**: Any Minimal API endpoint consuming JSON request bodies from web clients or polyglot services MUST support both camelCase and snake_case property keys.
- **Rationale**: Minimal APIs with `JsonNamingPolicy.SnakeCaseLower` ignore camelCase properties (e.g. `targetUserId`) by default when binding to C# records, causing null values and unexpected `validation_error` rejections.
- **Pattern**:
  ```csharp
  public sealed class SwitchUserApiRequest
  {
      [JsonPropertyName("target_user_id")]
      public string? TargetUserIdSnake { get; set; }

      [JsonPropertyName("targetUserId")]
      public string? TargetUserIdCamel { get; set; }

      [JsonIgnore]
      public string? TargetUserId => !string.IsNullOrWhiteSpace(TargetUserIdCamel)
          ? TargetUserIdCamel
          : TargetUserIdSnake;

      public SwitchUserApiRequest() { }
      public SwitchUserApiRequest(string? targetUserId)
      {
          TargetUserIdCamel = targetUserId;
          TargetUserIdSnake = targetUserId;
      }
  }
  ```



## 12. Configuration Store Schema & Persistence Invariant
- **Seed Parity**: Any configuration or setting store (`JsonConfigurationStore`, `ICostAllocationStore`) mapped to `GET /api/configuration` must initialize from authoritative repository seed files (`Backend/data/configuration.json`) or construct an equivalent complete schema (`config`, `departments`, `users`, `auditLogs`, `integrations`) with backward-compatible legacy keys (`company_name`, `currency`, `vat_rate_bps`).
- **Disk Synchronization**: Mutations performed via `SaveConfiguration`, `POST /api/configuration/departments`, or `POST /api/configuration/users` must update in-memory state and persist to the underlying JSON file on disk, preventing state loss across runtime restarts or container recycling.
