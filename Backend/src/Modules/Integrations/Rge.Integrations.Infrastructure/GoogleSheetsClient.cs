using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Domain;

namespace Rge.Integrations.Infrastructure;

public sealed class GoogleSheetsClient : IGoogleSheetsClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleSheetsClient> _logger;
    private readonly ConcurrentBag<IReadOnlyList<object?>> _appendedRows = new();

    public GoogleSheetsClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GoogleSheetsClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task AppendRowAsync(IReadOnlyList<object?> row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        _appendedRows.Add(row);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GoogleFormSubmission>> ReadSubmissionsAsync(
        string? spreadsheetId = null,
        string? range = null,
        CancellationToken cancellationToken = default)
    {
        return FetchSheetSubmissionsAsync(spreadsheetId, cancellationToken);
    }

    public async Task<IReadOnlyList<GoogleFormSubmission>> FetchSheetSubmissionsAsync(
        string? spreadsheetIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        var spreadsheetId = !string.IsNullOrWhiteSpace(spreadsheetIdOverride)
            ? spreadsheetIdOverride.Trim()
            : _configuration["GoogleForms:SpreadsheetId"]
              ?? _configuration["GOOGLE_FORMS_SPREADSHEET_ID"]
              ?? _configuration["FALLBACK_SPREADSHEET_ID"];

        if (string.IsNullOrWhiteSpace(spreadsheetId))
        {
            _logger.LogWarning("[GOOGLE-SHEETS-SYNC] Google Sheets SpreadsheetId is not configured. Skipping remote sheet download.");
            return Array.Empty<GoogleFormSubmission>();
        }

        var csvUrl = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv";
        _logger.LogInformation("[GOOGLE-SHEETS-SYNC] Fetching sheet CSV from configured spreadsheet.");

        try
        {
            using var response = await _httpClient.GetAsync(csvUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[GOOGLE-SHEETS-SYNC] Failed to download CSV from sheet: HTTP {StatusCode}",
                    (int)response.StatusCode);
                return Array.Empty<GoogleFormSubmission>();
            }

            var csvContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(csvContent))
            {
                return Array.Empty<GoogleFormSubmission>();
            }

            return SheetRowMapper.ParseCsvSubmissions(csvContent);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "[GOOGLE-SHEETS-SYNC] Network error while fetching Google Sheets CSV.");
            return Array.Empty<GoogleFormSubmission>();
        }
    }

    public IReadOnlyCollection<IReadOnlyList<object?>> AppendedRows => _appendedRows;
}
