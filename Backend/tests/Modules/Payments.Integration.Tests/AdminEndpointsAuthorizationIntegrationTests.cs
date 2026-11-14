using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Integration tests verifying Requirement R2:
/// Strict admin policy authorization on all /api/v1/admin route group endpoints.
/// Anonymous requests must receive 401 Unauthorized.
/// Authenticated non-admin actors must receive 403 Forbidden.
/// Authenticated admin and finance_admin actors must receive 200 OK.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class AdminEndpointsAuthorizationIntegrationTests : IClassFixture<AccountingWebApplicationFactory>, IDisposable
{
    private readonly AccountingWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminEndpointsAuthorizationIntegrationTests(AccountingWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private HttpClient CreateAnonymousClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Clear();
        return client;
    }

    private HttpClient CreateNonAdminClient(string userId = "employee_jane", string role = "employee")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("X-User-ID", userId);
        client.DefaultRequestHeaders.Add("X-User-Roles", role);
        return client;
    }

    private HttpClient CreateAdminClient(string userId = "acme_admin", string roles = "admin,finance_admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("X-User-ID", userId);
        client.DefaultRequestHeaders.Add("X-User-Roles", roles);
        return client;
    }

    [Theory]
    [InlineData("/api/v1/admin/webhook-events")]
    [InlineData("/api/v1/admin/fulfillment-outbox")]
    public async Task GetEndpoints_Unauthenticated_Returns401Unauthorized(string endpoint)
    {
        using var client = CreateAnonymousClient();
        var response = await client.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/fulfillment-outbox/process")]
    [InlineData("/api/v1/admin/payments/reconcile-all")]
    [InlineData("/api/v1/admin/payments/att_fake_test/reconcile")]
    public async Task PostEndpoints_Unauthenticated_Returns401Unauthorized(string endpoint)
    {
        using var client = CreateAnonymousClient();
        var response = await client.PostAsync(endpoint, null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/webhook-events")]
    [InlineData("/api/v1/admin/fulfillment-outbox")]
    public async Task GetEndpoints_AuthenticatedNonAdmin_Returns403Forbidden(string endpoint)
    {
        using var client = CreateNonAdminClient();
        var response = await client.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/fulfillment-outbox/process")]
    [InlineData("/api/v1/admin/payments/reconcile-all")]
    [InlineData("/api/v1/admin/payments/att_fake_test/reconcile")]
    public async Task PostEndpoints_AuthenticatedNonAdmin_Returns403Forbidden(string endpoint)
    {
        using var client = CreateNonAdminClient();
        var response = await client.PostAsync(endpoint, null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminActor_CanAccessWebhookEvents()
    {
        using var client = CreateAdminClient();
        var response = await client.GetAsync("/api/v1/admin/webhook-events");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminActor_CanAccessFulfillmentOutbox()
    {
        using var client = CreateAdminClient();
        var response = await client.GetAsync("/api/v1/admin/fulfillment-outbox");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminActor_CanProcessFulfillmentOutbox()
    {
        using var client = CreateAdminClient();
        var response = await client.PostAsync("/api/v1/admin/fulfillment-outbox/process", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminActor_CanReconcileAllPayments()
    {
        using var client = CreateAdminClient();
        var response = await client.PostAsync("/api/v1/admin/payments/reconcile-all", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FinanceAdminActor_CanAccessWebhookEvents()
    {
        using var client = CreateAdminClient("fin_lead", "finance_admin");
        var response = await client.GetAsync("/api/v1/admin/webhook-events");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FinanceAdminActor_CanAccessFulfillmentOutbox()
    {
        using var client = CreateAdminClient("fin_lead", "finance_admin");
        var response = await client.GetAsync("/api/v1/admin/fulfillment-outbox");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FinanceAdminActor_CanReconcileAllPayments()
    {
        using var client = CreateAdminClient("fin_lead", "finance_admin");
        var response = await client.PostAsync("/api/v1/admin/payments/reconcile-all", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
