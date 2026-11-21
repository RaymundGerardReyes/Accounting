namespace Rge.Integrations.Application.Ports;

public sealed record KotobeeUserPermissionResult(
    bool UserExists,
    string? Uid,
    string? Email,
    string BookAccess, // "full", "partial", "none"
    string RawResponse
);

public sealed record KotobeeMutationResult(
    bool Success,
    string? Uid,
    string? ErrorMessage,
    string RawResponse
);

public sealed record KotobeeBookSummary(
    string BookId,
    string Title
);

public sealed record KotobeeFulfillmentJob(
    string OrderId,
    string BuyerEmail,
    string? BuyerName,
    IReadOnlyList<string> PurchasedProductSkus
);

public sealed record KotobeeEntitlementRecord(
    Guid Id,
    string OrderId,
    string ProductSku,
    string BuyerEmail,
    string? KotobeeUid,
    string KotobeeMode,
    string? KotobeeLibId,
    string KotobeeBookId,
    string Status,
    string? BookAccessState,
    int AttemptCount,
    string? LastError,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? ActivatedAtUtc
);

public interface IKotobeeApiClient
{
    Task<KotobeeUserPermissionResult> GetUserAsync(
        string? email,
        string? uid,
        string? libraryId,
        string? bookId,
        CancellationToken ct = default);

    Task<KotobeeMutationResult> AddUserOrGrantBookAsync(
        string? email,
        string? uid,
        string? fullName,
        string? libraryId,
        string bookId,
        DateOnly? accessExpirationDate,
        CancellationToken ct = default);

    Task<KotobeeMutationResult> EditUserBookAccessAsync(
        string uid,
        string? libraryId,
        string bookId,
        bool active,
        DateOnly? accessExpirationDate,
        CancellationToken ct = default);

    Task<KotobeeMutationResult> RevokeBookAccessAsync(
        string? email,
        string? uid,
        string? libraryId,
        string bookId,
        CancellationToken ct = default);

    Task<IReadOnlyList<KotobeeBookSummary>> ListBooksAsync(
        string? libraryId,
        CancellationToken ct = default);
}

public interface IKotobeeEntitlementRepository
{
    Task UpsertPendingAsync(KotobeeEntitlementRecord record, CancellationToken ct = default);
    Task MarkActiveAsync(string orderId, string bookId, string? uid, string bookAccess, CancellationToken ct = default);
    Task MarkFailedAsync(string orderId, string bookId, string error, CancellationToken ct = default);
    Task MarkRevokedAsync(string orderId, string bookId, CancellationToken ct = default);
    Task<IReadOnlyList<KotobeeEntitlementRecord>> GetByOrderIdAsync(string orderId, CancellationToken ct = default);
}

public interface IKotobeeFulfillmentQueue
{
    ValueTask EnqueueAsync(KotobeeFulfillmentJob job, CancellationToken ct = default);
    IAsyncEnumerable<KotobeeFulfillmentJob> DequeueAllAsync(CancellationToken ct = default);
}

/// <summary>
/// Anti-corruption adapter to verify an order is STILL Paid in Rge.Payments.Contracts
/// immediately before granting Kotobee access.
/// </summary>
public interface IPaidOrderVerifier
{
    Task<bool> IsOrderConfirmedPaidAsync(string orderId, CancellationToken ct = default);
}

public sealed record KotobeeSheetCatalogSnapshot(
    Guid Id,
    string SpreadsheetId,
    string SheetRange,
    string ContentSha256,
    int ActiveRowCount,
    string MappingsJson,
    string SyncedBy,
    DateTimeOffset SyncedAtUtc,
    bool IsCurrent
);

public interface IKotobeeSheetSnapshotStore
{
    Task SaveSnapshotAsync(KotobeeSheetCatalogSnapshot snapshot, CancellationToken ct = default);
    Task<KotobeeSheetCatalogSnapshot?> GetLatestCurrentSnapshotAsync(CancellationToken ct = default);
    Task<IReadOnlyList<KotobeeSheetCatalogSnapshot>> GetAllSnapshotsAsync(int limit = 50, CancellationToken ct = default);
}

public sealed record TamperAlertEvent(
    string Operation,
    string TableName,
    string RecordId,
    string ActorId,
    string AuditReason,
    string? CorrelationId,
    string? Details
);

public interface ITamperAlertSink
{
    Task RecordTamperAttemptAsync(TamperAlertEvent alertEvent, CancellationToken ct = default);
}

public interface IAdminAlertEmailSender
{
    Task SendSecurityAlertAsync(string subject, string bodyHtml, CancellationToken ct = default);
}
