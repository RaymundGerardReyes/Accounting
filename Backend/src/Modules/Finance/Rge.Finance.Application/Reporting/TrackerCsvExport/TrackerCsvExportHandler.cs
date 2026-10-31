using System.Globalization;
using System.Text;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Reporting.Tracker;

namespace Rge.Finance.Application.Reporting.TrackerCsvExport;

public sealed record ExportTrackerCsvQuery(
    string? Workflow = null,
    string? DepartmentId = null,
    string? Status = null) : IQuery<byte[]>;

public sealed class TrackerCsvExportHandler : IQueryHandler<ExportTrackerCsvQuery, byte[]>
{
    private readonly TrackerHandler _trackerHandler;

    public TrackerCsvExportHandler(TrackerHandler trackerHandler)
    {
        ArgumentNullException.ThrowIfNull(trackerHandler);
        _trackerHandler = trackerHandler;
    }

    public async Task<Result<byte[]>> HandleAsync(
        ExportTrackerCsvQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var trackerQuery = new GetCentralizedTrackerQuery(query.Workflow, query.DepartmentId, query.Status, null, 1000);
        Result<IReadOnlyList<TrackerItemDto>> itemsResult = await _trackerHandler.HandleAsync(trackerQuery, cancellationToken).ConfigureAwait(false);
        if (itemsResult.IsFailure)
        {
            return Result.Failure<byte[]>(itemsResult.Error);
        }

        var sb = new StringBuilder();
        // UTF-8 BOM
        sb.Append('\uFEFF');
        sb.AppendLine("record_type,record_number,department_id,employee_id,vendor_name,amount_minor,currency,status,stage,created_at_utc");

        foreach (var item in itemsResult.Value)
        {
            sb.AppendLine(string.Join(',',
                EscapeCell(item.RecordType),
                EscapeCell(item.RecordNumber),
                EscapeCell(item.DepartmentId),
                EscapeCell(item.EmployeeId),
                EscapeCell(item.VendorName),
                item.AmountMinor.ToString(CultureInfo.InvariantCulture),
                EscapeCell(item.Currency),
                EscapeCell(item.Status),
                EscapeCell(item.Stage),
                item.CreatedAtUtc.ToString("o", CultureInfo.InvariantCulture)));
        }

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Result.Success(bytes);
    }

    private static string EscapeCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string val = value.Trim();
        if (val.StartsWith('=') || val.StartsWith('+') || val.StartsWith('-') || val.StartsWith('@'))
        {
            val = $"'{val}";
        }

        if (val.Contains(',', StringComparison.Ordinal) ||
            val.Contains('"', StringComparison.Ordinal) ||
            val.Contains('\n', StringComparison.Ordinal) ||
            val.Contains('\r', StringComparison.Ordinal))
        {
            return $"\"{val.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return val;
    }
}
