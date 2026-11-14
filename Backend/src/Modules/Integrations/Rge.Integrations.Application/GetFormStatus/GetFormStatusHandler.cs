using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Contracts;

namespace Rge.Integrations.Application.GetFormStatus;

public sealed record GetGoogleFormsStatusQuery : IQuery<GoogleFormsStatusResponse>;
public sealed record GetFormStatusQuery : IQuery<GoogleFormStatusResponse>;

public sealed class GetFormStatusHandler :
    IQueryHandler<GetGoogleFormsStatusQuery, GoogleFormsStatusResponse>,
    IQueryHandler<GetFormStatusQuery, GoogleFormStatusResponse>
{
    private readonly IGoogleFormsSubmissionStore _store;
    private readonly IConfiguration _configuration;
    private readonly ISecretStore? _secretStore;
    private readonly IGoogleFormOrderBridge? _orderBridge;

    public GetFormStatusHandler(
        IGoogleFormsSubmissionStore store,
        IConfiguration configuration,
        ISecretStore? secretStore = null,
        IGoogleFormOrderBridge? orderBridge = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(configuration);
        _store = store;
        _configuration = configuration;
        _secretStore = secretStore;
        _orderBridge = orderBridge;
    }

    public async Task<Result<GoogleFormsStatusResponse>> HandleAsync(
        GetGoogleFormsStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var configuredSecret = _secretStore?.GetSecret("GOOGLE_FORMS_SECRET")
            ?? _configuration["GoogleForms:WebhookSecret"]
            ?? _configuration["GOOGLE_FORMS_SECRET"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_FORMS_SECRET");

        var hasSecret = !string.IsNullOrWhiteSpace(configuredSecret);
        string? maskedSecret = null;

        if (hasSecret && configuredSecret is not null)
        {
            var trimmed = configuredSecret.Trim();
            maskedSecret = trimmed.Length > 4
                ? $"••••••••{trimmed[^4..]}"
                : "••••••••";
        }

        var storeStatus = await _store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        int total = storeStatus.IngestedOrdersCount;
        int paid = storeStatus.PaidOrdersCount;
        int pending = storeStatus.PendingOrdersCount;

        if (_orderBridge is not null)
        {
            var (bridgeTotal, bridgePaid, bridgePending) = await _orderBridge
                .GetGoogleFormOrderStatsAsync(cancellationToken)
                .ConfigureAwait(false);

            total = Math.Max(bridgeTotal, total);
            paid = bridgePaid;
            pending = bridgePending;
        }

        var response = new GoogleFormsStatusResponse(
            Status: "active",
            IngestedOrdersCount: total,
            PaidOrdersCount: paid,
            PendingOrdersCount: pending,
            SecretConfigured: hasSecret,
            MaskedSecret: maskedSecret,
            Enabled: true,
            WebhookEndpoint: "/api/v1/integrations/google-forms/submit",
            AuthHeaderName: "X-Google-Forms-Secret",
            HeartbeatInterval: "Active (4s interval)",
            ScriptTemplatePath: "scripts/google_apps_script/Code.gs");

        return Result.Success(response);
    }

    public async Task<Result<GoogleFormStatusResponse>> HandleAsync(
        GetFormStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var statusResult = await HandleAsync(new GetGoogleFormsStatusQuery(), cancellationToken).ConfigureAwait(false);
        var r = statusResult.Value;

        var response = new GoogleFormStatusResponse(
            Enabled: r.Enabled,
            WebhookEndpoint: r.WebhookEndpoint,
            AuthHeaderName: r.AuthHeaderName,
            IngestedOrdersCount: r.IngestedOrdersCount,
            HeartbeatInterval: r.HeartbeatInterval,
            ScriptTemplatePath: r.ScriptTemplatePath,
            SecretConfigured: r.SecretConfigured,
            MaskedSecret: r.MaskedSecret);

        return Result.Success(response);
    }
}
