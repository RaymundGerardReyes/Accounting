# Comprehensive Refactoring Guide: Heroku Startup Resilience & Graceful Worker Shutdown

## 1. Executive Summary & Incident Anatomy

### 1.1 Overview
During production deployment to Heroku dynos, the `Rge.Accounting.Host` service encountered two critical runtime failure modes:
1. **Fatal Startup Crash**: The application failed to boot, crashing immediately during host initialization before serving any HTTP traffic.
2. **Ungraceful Teardown & Dyno Cycling Crashes**: When the host attempted to restart, recycle dynos, or terminate on SIGTERM, background hosted services threw unhandled `OperationCanceledException`s, masking primary errors and triggering secondary crash alerts.

### 1.2 Incident Timeline & Symptoms

#### Symptom A: Host Startup Parameter Binding Crash
When ASP.NET Core built its route endpoint routing tables, the host crashed with the following verbatim exception:
```text
System.InvalidOperationException: Body was inferred but the method does not allow inferred body parameters.
Below is the list of parameters that we found:

Parameter           | Source
---------------------------------------------------------------------------------
period              | Query String (Inferred)
status              | Query String (Inferred)
source_module       | Query String (Inferred)
limit               | Query String (Inferred)
pipeline            | Body (Inferred)
handler             | Services (Inferred)

Did you mean to register the "Body (Inferred)" parameter(s) as a Service or apply the [FromServices] or [FromBody] attribute?
   at Microsoft.AspNetCore.Http.RequestDelegateFactory.CreateArguments(ParameterInfo[] parameters, RequestDelegateFactoryContext factoryContext)
   at Microsoft.AspNetCore.Http.RequestDelegateFactory.CreateArgumentsAndInferMetadata(MethodInfo methodInfo, RequestDelegateFactoryContext factoryContext)
   at Microsoft.AspNetCore.Http.RequestDelegateFactory.InferMetadata(MethodInfo methodInfo, RequestDelegateFactoryOptions options)
   at Microsoft.AspNetCore.Routing.RouteEndpointDataSource.CreateRouteEndpointBuilder(...)
   at Microsoft.AspNetCore.Routing.RouteEndpointDataSource.get_Endpoints()
   at Microsoft.AspNetCore.Routing.CompositeEndpointDataSource.EnsureEndpointsInitialized()
   at Microsoft.AspNetCore.Hosting.GenericWebHostService.StartAsync(CancellationToken cancellationToken)
```
This failure prevented the Heroku web dyno from binding to `$PORT` within the 60-second boot timeout window, causing dyno crash code `R10 (Boot timeout)` and `H10 (App crashed)`.

#### Symptom B: Secondary Worker Shutdown Crash
During host shutdown or abort, `ReceiptEmailBackgroundWorker` failed with an uncaught `OperationCanceledException`:
```text
System.OperationCanceledException: The operation was canceled.
   at System.Threading.Channels.AsyncOperation`1.GetResult(Int16 token)
   at System.Threading.Channels.ChannelReader`1.ReadAllAsync(...)
   at Rge.Receipts.Infrastructure.Mail.ReceiptEmailBackgroundWorker.ConsumeQueueLoopAsync(Int32 workerId, CancellationToken stoppingToken)
   at Rge.Receipts.Infrastructure.Mail.ReceiptEmailBackgroundWorker.ExecuteAsync(CancellationToken stoppingToken)
```
Because the exception escaped `ExecuteAsync`, ASP.NET Core Generic Host's default `BackgroundServiceExceptionBehavior.StopHost` treated it as an unhandled fault, logging error-level alerts and causing noisy, non-zero exits on dyno recycling.

---

## 2. Root Cause Analysis (Deep Dive)

### 2.1 ASP.NET Core Minimal API Parameter Inference Mechanics
ASP.NET Core Minimal APIs compile route delegates at startup using `Microsoft.AspNetCore.Http.RequestDelegateFactory` (RDF). For each parameter on a route handler lambda, RDF determines the parameter source using a strict sequence of heuristics:

1. **Special Contextual Types**: `HttpContext`, `HttpRequest`, `HttpResponse`, `ClaimsPrincipal`, and `CancellationToken` are resolved directly from the request context.
2. **Explicit Binding Attributes**: If an attribute such as `[FromQuery]`, `[FromHeader]`, `[FromRoute]`, `[FromBody]`, or `[FromServices]` is present, RDF binds strictly according to that attribute.
3. **DI Services Check**: For unadorned parameters, RDF checks `IServiceProviderIsService.IsService(parameterType)` using the root service provider. If true, the parameter is treated as `[FromServices]`.
4. **Parsable / Primitive Types**: Types implementing `TryParse`, `IParsable<T>`, or `IBindableFromHttpContext<T>` (along with primitive types and strings) default to route or query string parameters.
5. **Fallback for Unregistered Complex Types**: Any complex object not matching rules 1–4 is inferred as coming from the HTTP Request Body (`Source: Body (Inferred)`).

#### The Fatal Clash in `GeneralLedgerEndpoints.cs`
In `GeneralLedgerEndpoints.cs`:
- The GET routes `/api/v1/finance/gl/journals` and `/api/v1/finance/gl/trial-balance` declared a parameter:
  ```csharp
  RequestPipelineExecutor pipeline
  ```
- `RequestPipelineExecutor` is a complex class.
- Crucially, `RequestPipelineExecutor` **was not registered in the DI container** (`IServiceCollection`).
- Consequently, `IServiceProviderIsService.IsService(typeof(RequestPipelineExecutor))` returned `false`.
- RDF fell back to rule 5: `pipeline` was inferred as `Body (Inferred)`.
- **The Invariant Violation**: HTTP specifications and ASP.NET Core disallow request bodies on HTTP GET methods. When RDF compiled the endpoints during `CompositeEndpointDataSource.EnsureEndpointsInitialized()` on host startup, it threw `InvalidOperationException`, terminating the process.

#### Secondary Dependency Injection Gap
Even if `[FromServices]` had been added to `pipeline`, resolving `RequestPipelineExecutor` would have failed at runtime:
```csharp
public RequestPipelineExecutor(
    ICurrentActor currentActor,
    IUnitOfWork unitOfWork,
    IApplicationCache cache,
    IAuditBehaviorWriter auditWriter)
```
While `ICurrentActor`, `IUnitOfWork`, and `IAuditBehaviorWriter` were registered, `IApplicationCache` was not: `ServiceCollectionExtensions.cs` registered concrete `CacheInvalidator` (`services.AddSingleton<CacheInvalidator>()`), but omitted `services.AddSingleton<IApplicationCache>(sp => sp.GetRequiredService<CacheInvalidator>())`.

### 2.2 Hosted Services & Asynchronous Channel Cancellation Mechanics
`ReceiptEmailBackgroundWorker` is an `IHostedService` inheriting from `BackgroundService`. It uses bounded System.Threading.Channels (`ChannelReceiptEmailQueue`) to dispatch receipt emails asynchronously:

1. `ExecuteAsync` spawned 3 concurrent loops (`ConsumeQueueLoopAsync`).
2. Each loop iterated over `_queue.ReadAllAsync(stoppingToken)`:
   ```csharp
   await foreach (var job in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
   {
       await ProcessEmailJobWithRetryAsync(workerId, job, stoppingToken).ConfigureAwait(false);
   }
   ```
3. Internally, `ChannelReader<T>.ReadAllAsync(CancellationToken)` continuously awaits `WaitToReadAsync(stoppingToken)`.
4. When Heroku sends `SIGTERM` or the host shuts down, the Generic Host cancels `stoppingToken`.
5. Awaiting `WaitToReadAsync` on an empty or idle channel with a canceled token immediately throws `OperationCanceledException`.
6. Because `ConsumeQueueLoopAsync` had no `try / catch` around the channel consumption loop, `OperationCanceledException` escaped into `Task.WhenAll(consumers)`.
7. `ExecuteAsync` directly returned `Task.WhenAll(consumers)` without catching `OperationCanceledException`, setting `worker.ExecuteTask` to a Canceled/Faulted state.
8. The host framework interpreted this as an unhandled background service failure, logging fatal errors during dyno teardown.

---

## 3. Codebase Changes & Architecture Refactor

### 3.1 Refactoring `GeneralLedgerEndpoints.cs`
File: `Backend/src/Modules/Finance/Rge.Finance.Api/GeneralLedger/GeneralLedgerEndpoints.cs`

1. **Direct Handler Invocation**: Handlers (`GetGeneralLedgerJournalsHandler`, `GetGeneralLedgerTrialBalanceHandler`, `CreateManualJournalHandler`, `DecideJournalApprovalHandler`) are invoked directly, matching sibling modules like `CashAdvanceEndpoints.cs`.
2. **Removed Route Parameter**: `RequestPipelineExecutor pipeline` was removed from all endpoint parameter signatures, eliminating any potential for `Body (Inferred)`.
3. **Explicit Parameter Attributes**: All endpoint parameters are explicitly decorated with `[FromQuery]`, `[FromServices]`, `[FromBody]`, and `[FromRoute]`:
   - `/api/v1/finance/gl/accounts`: `[FromServices] IGeneralLedgerRepository repository`
   - `/api/v1/finance/gl/journals` (GET): `[FromQuery] string period, [FromQuery] string? status, [FromQuery] string? source_module, [FromQuery] int? limit, HttpContext httpContext, [FromServices] GetGeneralLedgerJournalsHandler handler`
   - `/api/v1/finance/gl/trial-balance` (GET): `[FromQuery] string period, [FromQuery] string? department_id, HttpContext httpContext, [FromServices] GetGeneralLedgerTrialBalanceHandler handler`
   - `/api/v1/finance/gl/journals` (POST): `[FromBody] CreateManualJournalRequest request, HttpContext httpContext, [FromServices] CreateManualJournalHandler handler`
   - `/api/v1/finance/gl/journals/{journalId:guid}/decide` (POST): `[FromRoute] Guid journalId, [FromQuery] string prepared_by_actor_id, [FromBody] DecideJournalApprovalRequest request, HttpContext httpContext, [FromServices] DecideJournalApprovalHandler handler`

### 3.2 Service Container & Host Hardening
1. **`IApplicationCache` Registration**:
   In `Backend/src/Host/Rge.Accounting.Host/Composition/ServiceCollectionExtensions.cs`:
   ```csharp
   services.AddSingleton<CacheInvalidator>();
   services.AddSingleton<IApplicationCache>(sp => sp.GetRequiredService<CacheInvalidator>());
   ```
2. **`RequestPipelineExecutor` Registration**:
   In `Backend/src/Host/Rge.Accounting.Host/Composition/ServiceCollectionExtensions.cs` and `Backend/src/Modules/Finance/Rge.Finance.Infrastructure/DependencyInjection.cs`:
   ```csharp
   services.AddScoped<RequestPipelineExecutor>();
   ```
3. **Startup Scope & Build Validation**:
   In `Backend/src/Host/Rge.Accounting.Host/Program.cs`:
   ```csharp
   builder.Host.UseDefaultServiceProvider((context, options) =>
   {
       bool validate = context.HostingEnvironment.IsDevelopment() || context.HostingEnvironment.IsEnvironment("Testing");
       options.ValidateScopes = validate;
       options.ValidateOnBuild = validate;
   });
   ```
   This guarantees that missing singletons, missing dependencies, or captive scoped dependencies are caught at application build time rather than at runtime in production.

### 3.3 Graceful Background Worker Shutdown
File: `Backend/src/Modules/Receipts/Rge.Receipts.Infrastructure/Mail/ReceiptEmailBackgroundWorker.cs`

1. **Channel Consumption Loop Protection**:
   In `ConsumeQueueLoopAsync`, wrapped the `await foreach` loop:
   ```csharp
   private async Task ConsumeQueueLoopAsync(int workerId, CancellationToken stoppingToken)
   {
       try
       {
           await foreach (var job in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
           {
               await ProcessEmailJobWithRetryAsync(workerId, job, stoppingToken).ConfigureAwait(false);
           }
       }
       catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
       {
           // Graceful shutdown: stoppingToken signaled cancellation while reading from channel
       }
   }
   ```
2. **Outer Execution Task Protection**:
   In `ExecuteAsync`, wrapped the `Task.WhenAll` invocation:
   ```csharp
   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
       try
       {
           var consumers = Enumerable.Range(1, WorkerConcurrency)
               .Select(workerId => ConsumeQueueLoopAsync(workerId, stoppingToken))
               .ToArray();

           await Task.WhenAll(consumers).ConfigureAwait(false);
       }
       catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
       {
           _logger.LogInformation("[RECEIPT-WORKER-STOPPED] Receipt email background worker stopped gracefully.");
       }
   }
   ```
This ensures `worker.ExecuteTask` transitions to `RanToCompletion` (`IsCompletedSuccessfully == true`) without surfacing unhandled exceptions or faulting during host shutdown.

---

## 4. Automated Guardrail Architecture (R3)

To permanently protect against regressions, three layers of automated guardrails were implemented:

### 4.1 Guardrail 1: Endpoint Materialization Smoke Tests
File: `Backend/tests/Modules/Payments.Integration.Tests/EndpointMaterializationSmokeTests.cs`
- **Mechanism**: Boots the ASP.NET Core host in-memory via `AccountingWebApplicationFactory`.
- **Validation**: Enumerates every `RouteEndpoint` in `EndpointDataSource` and forces evaluation of `endpoint.RequestDelegate`. If any Minimal API endpoint has an unresolvable parameter, missing service descriptor, or invalid `[FromBody]` inference on a GET route, this test fails immediately with the exact parameter name and source.
- **Route Execution**: Invokes GET `/api/v1/finance/gl/accounts`, `/api/v1/finance/gl/journals?period=2026-11`, and `/api/v1/finance/gl/trial-balance?period=2026-11`, verifying HTTP 200 OK responses.

### 4.2 Guardrail 2: Dependency Injection & Scope Validation Tests
File: `Backend/tests/Modules/Payments.Integration.Tests/DependencyInjectionValidationTests.cs`
- **Scoped Service Resolution**: Creates an async service scope and resolves all pipeline behaviors, infrastructure singletons, repositories, and Finance handlers:
  - `IApplicationCache`, `ICurrentActor`, `IUnitOfWork`, `IAuditBehaviorWriter`, `RequestPipelineExecutor`
  - `GetGeneralLedgerJournalsHandler`, `GetGeneralLedgerTrialBalanceHandler`, `CreateManualJournalHandler`, `DecideJournalApprovalHandler`
  - `IGeneralLedgerRepository`, `ISubledgerGlPostingService`
- **Scope Boundary Enforcement**: Verifies that resolving scoped services (`RequestPipelineExecutor`) directly from the root `ServiceProvider` throws `InvalidOperationException`, confirming `ValidateScopes` is actively protecting against captive scoped dependencies.

### 4.3 Guardrail 3: Background Worker Cancellation Tests
File: `Backend/tests/Modules/Receipts.Integration.Tests/ReceiptEmailBackgroundWorkerCancellationTests.cs`
- **Graceful Stop Verification**: Starts `ReceiptEmailBackgroundWorker`, allows the 3 consumer loops to enter `_queue.ReadAllAsync`, and signals `worker.StopAsync(cancellationToken)`.
- **Clean Task Completion**: Asserts that `worker.ExecuteTask` completes successfully with:
  ```csharp
  Assert.True(worker.ExecuteTask.IsCompleted);
  Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
  Assert.False(worker.ExecuteTask.IsFaulted);
  ```
- **Direct Token Cancellation**: Tests starting the worker with an already-canceled or rapidly-canceled token, proving zero unhandled exceptions surface during abort sequences.

---

## 5. Verification, Deployment & Operations Runbook

### 5.1 Local Build & Test Verification Commands

1. **Compile Solution**:
   ```powershell
   $env:MSBUILDDISABLENODEREUSE="1"; $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER="1"
   cd Backend
   dotnet build .\Rge.Accounting.slnx --no-restore --disable-build-servers -m:1 /nr:false
   ```
   *Expected Result*: Build succeeded with 0 Warning(s) and 0 Error(s).

2. **Execute Guardrail Tests**:
   ```powershell
   # 1. Endpoint Materialization Smoke Tests
   dotnet test tests/Modules/Payments.Integration.Tests/Rge.Payments.Integration.Tests.csproj --no-build --no-restore --disable-build-servers -m:1 --filter "FullyQualifiedName~EndpointMaterializationSmokeTests"

   # 2. DI Scope Validation Tests
   dotnet test tests/Modules/Payments.Integration.Tests/Rge.Payments.Integration.Tests.csproj --no-build --no-restore --disable-build-servers -m:1 --filter "FullyQualifiedName~DependencyInjectionValidationTests"

   # 3. Receipt Worker Cancellation Tests
   dotnet test tests/Modules/Receipts.Integration.Tests/Rge.Receipts.Integration.Tests.csproj --no-build --no-restore --disable-build-servers -m:1 --filter "FullyQualifiedName~ReceiptEmailBackgroundWorkerCancellationTests"
   ```

3. **Execute Core Module Test Suites**:
   ```powershell
   # Finance Application Tests
   dotnet test tests/Modules/Finance.Application.Tests/Rge.Finance.Application.Tests.csproj --no-build --no-restore --disable-build-servers -m:1

   # Receipts Integration Tests
   dotnet test tests/Modules/Receipts.Integration.Tests/Rge.Receipts.Integration.Tests.csproj --no-build --no-restore --disable-build-servers -m:1

   # Architecture Tests
   dotnet test tests/Architecture.Tests/Rge.Architecture.Tests.csproj --no-build --no-restore --disable-build-servers -m:1
   ```

### 5.2 CI/CD Pipeline Integration
In GitHub Actions / CI workflow:
- Ensure the test step includes `--filter "FullyQualifiedName!~PostgreSqlLiveDatabaseLedgerTests"` for offline CI runs, or supplies `RGE_TEST_POSTGRES_CONN` when PostgreSQL service containers are attached.
- Add `ValidateScopes = true` in staging/testing environments to continuously reject invalid DI scopes on pull requests.

### 5.3 Heroku Deployment & Verification Runbook
1. Deploy release branch to Heroku:
   ```bash
   git push heroku main
   ```
2. Monitor startup logs:
   ```bash
   heroku logs --tail -a rge-accounting
   ```
3. Verify successful startup signatures:
   - `[INFO] Now listening on: http://0.0.0.0:$PORT`
   - `[INFO] Application started. Press Ctrl+C to shut down.`
   - No occurrences of `Body was inferred but the method does not allow inferred body parameters`.
4. Trigger dyno restart to verify graceful worker teardown:
   ```bash
   heroku restart web -a rge-accounting
   ```
   Verify logs confirm graceful shutdown:
   - `[RECEIPT-WORKER-STOPPED] Receipt email background worker stopped gracefully.`
   - Dyno exit code 0 (clean SIGTERM shutdown without crashes).
