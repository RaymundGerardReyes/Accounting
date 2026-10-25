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
