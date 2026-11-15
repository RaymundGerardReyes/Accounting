using System;
using System.Linq;
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
    private static readonly string[] SecretCandidateKeys =
    [
        "GOOGLE_FORMS_SECRET",
        "GOOGLE_FORMS_SHARED_SECRET",
        "Secrets:GoogleFormsSecret",
        "Secrets__GoogleFormsSecret",
        "GoogleForms:WebhookSecret",
        "GoogleForms:SharedSecret",
        "google_forms_secret"
    ];

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

        var snapshot = await _store.GetStatusSnapshotAsync(
            maxRecentLimit: 200,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var resolvedSecret = ResolveSecret();
        var secretConfigured = !string.IsNullOrWhiteSpace(resolvedSecret);
        var maskedSecret = MaskSecret(resolvedSecret);

        int total = snapshot.TotalSubmissions;
        int paid = snapshot.PaidOrdersCount;
        int pending = snapshot.PendingOrdersCount;

        if (_orderBridge is not null)
        {
            var (bridgeTotal, bridgePaid, bridgePending) = await _orderBridge
                .GetGoogleFormOrderStatsAsync(cancellationToken)
                .ConfigureAwait(false);

            total = Math.Max(bridgeTotal, total);
            paid = Math.Max(bridgePaid, paid);
            pending = Math.Max(0, total - paid);
        }

        return Result.Success(new GoogleFormsStatusResponse(
            Enabled: true,
            TotalSubmissions: total,
            SyncedCount: total,
            LastSyncedAt: snapshot.LastSyncedAt,
            RecentSubmissions: snapshot.RecentSubmissions,
            SecretConfigured: secretConfigured,
            MaskedSecret: maskedSecret,
            Status: "active",
            IngestedOrdersCount: total,
            PaidOrdersCount: paid,
            PendingOrdersCount: pending,
            WebhookEndpoint: "/api/v1/integrations/google-forms/submit",
            AuthHeaderName: "X-Google-Forms-Secret",
            HeartbeatInterval: "SignalR Real-Time Push (/hubs/payments)",
            ScriptTemplatePath: "scripts/google_apps_script/Code.gs"));
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

    private string? ResolveSecret()
    {
        foreach (var key in SecretCandidateKeys)
        {
            if (_secretStore is not null)
            {
                var fromStore = _secretStore.GetSecret(key);
                if (!string.IsNullOrWhiteSpace(fromStore))
                {
                    return fromStore.Trim();
                }
            }

            var fromConfig = _configuration[key];
            if (!string.IsNullOrWhiteSpace(fromConfig))
            {
                return fromConfig.Trim();
            }

            var fromEnv = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                return fromEnv.Trim();
            }
        }

        return null;
    }

    private static string? MaskSecret(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var trimmed = secret.Trim();
        var visibleSuffix = trimmed.Length <= 4
            ? trimmed
            : trimmed[^4..];

        return $"••••••••{visibleSuffix}";
    }
}
