using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rge.Integrations.Application.Kotobee;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Infrastructure.Kotobee;

public sealed class KotobeeApiClient : IKotobeeApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<KotobeeOptions> _optionsMonitor;
    private readonly ILogger<KotobeeApiClient> _logger;

    // Shared across calls to strictly enforce Kotobee's 1 req/sec limit
    private static readonly SemaphoreSlim RateLimitGate = new(1, 1);
    private static DateTimeOffset _lastRequestUtc = DateTimeOffset.MinValue;

    public KotobeeApiClient(
        HttpClient httpClient,
        IOptionsMonitor<KotobeeOptions> optionsMonitor,
        ILogger<KotobeeApiClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<KotobeeUserPermissionResult> GetUserAsync(
        string? email,
        string? uid,
        string? libraryId,
        string? bookId,
        CancellationToken ct = default)
    {
        var opts = _optionsMonitor.CurrentValue;
        var form = BuildAuthForm(opts);

        if (!string.IsNullOrWhiteSpace(uid))
        {
            form["uid"] = uid;
        }
        else if (!string.IsNullOrWhiteSpace(email))
        {
            form["email"] = email;
        }
        else
        {
            throw new ArgumentException("Either email or uid must be provided.");
        }

        ApplyTargetScope(opts, form, libraryId, bookId);

        var raw = await PostFormAsync("user/get", form, opts, ct).ConfigureAwait(false);

        using var doc = TryParseJson(raw);
        if (doc is null || IsErrorResponse(doc.RootElement, out _))
        {
            return new KotobeeUserPermissionResult(false, null, email, "none", raw);
        }

        var root = doc.RootElement;
        var resolvedUid = ReadString(root, "uid") ?? ReadString(root, "id");
        var resolvedEmail = ReadString(root, "email") ?? email;
        var bookAccess = (ReadString(root, "bookAccess") ?? "none").ToLowerInvariant();
        var exists = !string.IsNullOrWhiteSpace(resolvedUid) || !string.IsNullOrWhiteSpace(ReadString(root, "email"));

        return new KotobeeUserPermissionResult(exists, resolvedUid, resolvedEmail, bookAccess, raw);
    }

    public async Task<KotobeeMutationResult> AddUserOrGrantBookAsync(
        string? email,
        string? uid,
        string? fullName,
        string? libraryId,
        string bookId,
        DateOnly? accessExpirationDate,
        CancellationToken ct = default)
    {
        var opts = _optionsMonitor.CurrentValue;
        var form = BuildAuthForm(opts);

        // Kotobee rule: pass uid to grant additional book access to an existing user;
        // pass email to create a brand-new user with this book permission.
        if (!string.IsNullOrWhiteSpace(uid))
        {
            form["uid"] = uid;
            if (accessExpirationDate.HasValue)
            {
                form["accessexpirationdate"] = accessExpirationDate.Value.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            }
        }
        else if (!string.IsNullOrWhiteSpace(email))
        {
            form["email"] = email;
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                form["name"] = fullName;
            }
            if (accessExpirationDate.HasValue)
            {
                form["expirationdate"] = accessExpirationDate.Value.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            }
        }
        else
        {
            throw new ArgumentException("Either uid or email is required for /user/add.");
        }

        ApplyTargetScope(opts, form, libraryId, bookId);

        // Empty chapters value explicitly grants full-book access
        form["chapters"] = string.Empty;
        form["active"] = opts.ActivateUsersImmediately ? "1" : "0";

        var raw = await PostFormAsync("user/add", form, opts, ct).ConfigureAwait(false);
        return ParseMutationResult(raw);
    }

    public async Task<KotobeeMutationResult> EditUserBookAccessAsync(
        string uid,
        string? libraryId,
        string bookId,
        bool active,
        DateOnly? accessExpirationDate,
        CancellationToken ct = default)
    {
        var opts = _optionsMonitor.CurrentValue;
        var form = BuildAuthForm(opts);
        form["uid"] = uid;

        ApplyTargetScope(opts, form, libraryId, bookId);
        form["chapters"] = string.Empty;
        form["active"] = active ? "1" : "0";

        if (accessExpirationDate.HasValue)
        {
            form["accessexpirationdate"] = accessExpirationDate.Value.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        }

        var raw = await PostFormAsync("user/edit", form, opts, ct).ConfigureAwait(false);
        return ParseMutationResult(raw);
    }

    public async Task<KotobeeMutationResult> RevokeBookAccessAsync(
        string? email,
        string? uid,
        string? libraryId,
        string bookId,
        CancellationToken ct = default)
    {
        var opts = _optionsMonitor.CurrentValue;
        var form = BuildAuthForm(opts);

        if (!string.IsNullOrWhiteSpace(uid))
        {
            form["uid"] = uid;
        }
        else if (!string.IsNullOrWhiteSpace(email))
        {
            form["email"] = email;
        }
        else
        {
            throw new ArgumentException("Either uid or email is required for /user/delete.");
        }

        // IMPORTANT: Never pass deleteall=1 here, so unrelated books stay intact.
        ApplyTargetScope(opts, form, libraryId, bookId);

        var raw = await PostFormAsync("user/delete", form, opts, ct).ConfigureAwait(false);
        return ParseMutationResult(raw);
    }

    public async Task<IReadOnlyList<KotobeeBookSummary>> ListBooksAsync(
        string? libraryId,
        CancellationToken ct = default)
    {
        var opts = _optionsMonitor.CurrentValue;
        var form = BuildAuthForm(opts);
        var isCloud = string.Equals(opts.PublishingMode, "cloud", StringComparison.OrdinalIgnoreCase);

        form["type"] = isCloud ? "cloud" : "library";
        form["simple"] = "1";

        if (!isCloud)
        {
            var effectiveLibId = libraryId ?? opts.DefaultLibraryId
                ?? throw new InvalidOperationException("DefaultLibraryId is required in library mode.");
            form["libid"] = effectiveLibId;
        }

        var raw = await PostFormAsync("book/all", form, opts, ct).ConfigureAwait(false);
        using var doc = TryParseJson(raw);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var books = new List<KotobeeBookSummary>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var id = ReadString(item, "id") ?? ReadString(item, "bid") ?? ReadString(item, "cid");
            var title = ReadString(item, "name") ?? ReadString(item, "title") ?? "(Untitled)";
            if (!string.IsNullOrWhiteSpace(id))
            {
                books.Add(new KotobeeBookSummary(id, title));
            }
        }

        return books;
    }

    private static Dictionary<string, string> BuildAuthForm(KotobeeOptions opts)
    {
        var form = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(opts.AccessKey))
        {
            form["accesskey"] = opts.AccessKey;
        }
        else if (!string.IsNullOrWhiteSpace(opts.Serial))
        {
            form["serial"] = opts.Serial;
        }
        else
        {
            throw new InvalidOperationException("Kotobee AccessKey or Serial is not configured.");
        }

        return form;
    }

    private static void ApplyTargetScope(
        KotobeeOptions opts,
        Dictionary<string, string> form,
        string? libraryId,
        string? bookId)
    {
        var isCloud = string.Equals(opts.PublishingMode, "cloud", StringComparison.OrdinalIgnoreCase);
        if (isCloud)
        {
            if (!string.IsNullOrWhiteSpace(bookId))
            {
                form["cid"] = bookId;
            }
        }
        else
        {
            var effectiveLibId = libraryId ?? opts.DefaultLibraryId;
            if (!string.IsNullOrWhiteSpace(effectiveLibId))
            {
                form["libid"] = effectiveLibId;
            }

            if (!string.IsNullOrWhiteSpace(bookId))
            {
                form["bid"] = bookId;
            }
        }
    }

    private async Task<string> PostFormAsync(
        string relativeEndpoint,
        Dictionary<string, string> formFields,
        KotobeeOptions opts,
        CancellationToken ct)
    {
        await ThrottleAsync(opts.MinRequestIntervalMs, ct).ConfigureAwait(false);

        var url = $"{opts.BaseUrl.TrimEnd('/')}/{relativeEndpoint.TrimStart('/')}";
        using var content = new FormUrlEncodedContent(formFields);
        using var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Kotobee API {Endpoint} returned HTTP {StatusCode}.",
                relativeEndpoint, (int)response.StatusCode);
            throw new HttpRequestException(
                $"Kotobee API '{relativeEndpoint}' failed with HTTP {(int)response.StatusCode}.");
        }

        return body;
    }

    private static async Task ThrottleAsync(int minIntervalMs, CancellationToken ct)
    {
        await RateLimitGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var elapsed = DateTimeOffset.UtcNow - _lastRequestUtc;
            var waitMs = minIntervalMs - (int)elapsed.TotalMilliseconds;
            if (waitMs > 0)
            {
                await Task.Delay(waitMs, ct).ConfigureAwait(false);
            }

            _lastRequestUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            RateLimitGate.Release();
        }
    }

    private static KotobeeMutationResult ParseMutationResult(string raw)
    {
        using var doc = TryParseJson(raw);
        if (doc is null)
        {
            var ok = !raw.Contains("error", StringComparison.OrdinalIgnoreCase);
            return new KotobeeMutationResult(ok, null, ok ? null : raw, raw);
        }

        if (IsErrorResponse(doc.RootElement, out var errorMsg))
        {
            return new KotobeeMutationResult(false, null, errorMsg, raw);
        }

        var uid = ReadString(doc.RootElement, "uid") ?? ReadString(doc.RootElement, "id");
        return new KotobeeMutationResult(true, uid, null, raw);
    }

    private static JsonDocument? TryParseJson(string raw)
    {
        try
        {
            return JsonDocument.Parse(raw);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsErrorResponse(JsonElement root, out string? message)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("error", out var errProp) &&
                errProp.ValueKind is not JsonValueKind.False and not JsonValueKind.Null)
            {
                message = errProp.ToString();
                return true;
            }

            if (root.TryGetProperty("status", out var statusProp) &&
                string.Equals(statusProp.ToString(), "error", StringComparison.OrdinalIgnoreCase))
            {
                message = ReadString(root, "message") ?? "Kotobee returned status=error";
                return true;
            }
        }

        message = null;
        return false;
    }

    private static string? ReadString(JsonElement obj, string propName)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(propName, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            _ => null
        };
    }
}
