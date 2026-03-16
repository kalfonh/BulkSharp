using System.Text;
using System.Text.Json;
using BulkSharp.Core.Abstractions.Export;
using BulkSharp.Core.Domain.Export;

namespace BulkSharp.Processing.Export;

internal sealed class DefaultBulkExportFormatter : IBulkExportFormatter
{
    private static readonly string[] MetadataColumns =
    [
        "RowNumber", "RowId", "State", "StepName", "StepIndex",
        "ErrorType", "ErrorMessage", "RetryAttempt", "CreatedAt", "CompletedAt"
    ];

    public async Task<Stream> FormatReportAsync(
        IAsyncEnumerable<BulkExportRow> rows,
        ExportRequest request,
        CancellationToken ct = default)
    {
        var collected = new List<BulkExportRow>();
        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false))
            collected.Add(row);

        return request.Format == ExportFormat.Json
            ? FormatReportJson(collected)
            : FormatReportCsv(collected);
    }

    public async Task<Stream> FormatDataAsync(
        IAsyncEnumerable<BulkExportRow> rows,
        ExportRequest request,
        CancellationToken ct = default)
    {
        var collected = new List<BulkExportRow>();
        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false))
            collected.Add(row);

        return request.Format == ExportFormat.Json
            ? FormatDataJson(collected)
            : FormatDataCsv(collected);
    }

    private static Stream FormatReportCsv(List<BulkExportRow> rows)
    {
        var dynamicColumns = GetDynamicColumns(rows);
        var sb = new StringBuilder();

        // Header
        sb.AppendJoin(',', MetadataColumns);
        foreach (var col in dynamicColumns)
        {
            sb.Append(',');
            sb.Append(EscapeCsv(col));
        }
        sb.AppendLine();

        // Data rows
        foreach (var row in rows)
        {
            sb.Append(row.RowNumber);
            sb.Append(','); sb.Append(EscapeCsv(row.RowId));
            sb.Append(','); sb.Append(row.State);
            sb.Append(','); sb.Append(EscapeCsv(row.StepName));
            sb.Append(','); sb.Append(row.StepIndex);
            sb.Append(','); sb.Append(row.ErrorType?.ToString());
            sb.Append(','); sb.Append(EscapeCsv(row.ErrorMessage));
            sb.Append(','); sb.Append(row.RetryAttempt);
            sb.Append(','); sb.Append(row.CreatedAt.ToString("O"));
            sb.Append(','); sb.Append(row.CompletedAt?.ToString("O"));

            var rowDataProps = ParseRowData(row.RowData);
            foreach (var col in dynamicColumns)
            {
                sb.Append(',');
                if (rowDataProps != null && rowDataProps.TryGetValue(col, out var value))
                    sb.Append(EscapeCsv(value));
            }
            sb.AppendLine();
        }

        return ToStream(sb);
    }

    private static Stream FormatReportJson(List<BulkExportRow> rows)
    {
        var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });

        writer.WriteStartArray();
        foreach (var row in rows)
        {
            writer.WriteStartObject();
            writer.WriteNumber("RowNumber", row.RowNumber);
            WriteStringOrNull(writer, "RowId", row.RowId);
            writer.WriteString("State", row.State.ToString());
            WriteStringOrNull(writer, "StepName", row.StepName);
            writer.WriteNumber("StepIndex", row.StepIndex);
            WriteStringOrNull(writer, "ErrorType", row.ErrorType?.ToString());
            WriteStringOrNull(writer, "ErrorMessage", row.ErrorMessage);
            writer.WriteNumber("RetryAttempt", row.RetryAttempt);
            writer.WriteString("CreatedAt", row.CreatedAt.ToString("O"));
            if (row.CompletedAt.HasValue)
                writer.WriteString("CompletedAt", row.CompletedAt.Value.ToString("O"));
            else
                writer.WriteNull("CompletedAt");

            MergeRowDataProperties(writer, row.RowData);

            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.Flush();

        ms.Position = 0;
        return ms;
    }

    private static Stream FormatDataCsv(List<BulkExportRow> rows)
    {
        var dynamicColumns = GetDynamicColumns(rows);
        var sb = new StringBuilder();

        if (dynamicColumns.Count == 0)
            return ToStream(sb);

        // Header
        sb.AppendJoin(',', dynamicColumns.Select(EscapeCsv));
        sb.AppendLine();

        // Data rows
        foreach (var row in rows)
        {
            var rowDataProps = ParseRowData(row.RowData);
            var first = true;
            foreach (var col in dynamicColumns)
            {
                if (!first) sb.Append(',');
                first = false;
                if (rowDataProps != null && rowDataProps.TryGetValue(col, out var value))
                    sb.Append(EscapeCsv(value));
            }
            sb.AppendLine();
        }

        return ToStream(sb);
    }

    private static Stream FormatDataJson(List<BulkExportRow> rows)
    {
        var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });

        writer.WriteStartArray();
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.RowData))
            {
                writer.WriteNullValue();
                continue;
            }

            using var doc = JsonDocument.Parse(row.RowData);
            doc.RootElement.WriteTo(writer);
        }
        writer.WriteEndArray();
        writer.Flush();

        ms.Position = 0;
        return ms;
    }

    private static List<string> GetDynamicColumns(List<BulkExportRow> rows)
    {
        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.RowData))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(row.RowData);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (seen.Add(prop.Name))
                        columns.Add(prop.Name);
                }
            }
            catch (JsonException)
            {
                // Skip rows with invalid JSON
            }
        }

        return columns;
    }

    private static Dictionary<string, string>? ParseRowData(string? rowData)
    {
        if (string.IsNullOrEmpty(rowData))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(rowData);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? string.Empty
                    : prop.Value.GetRawText();
            }
            return dict;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void MergeRowDataProperties(Utf8JsonWriter writer, string? rowData)
    {
        if (string.IsNullOrEmpty(rowData))
            return;

        try
        {
            using var doc = JsonDocument.Parse(rowData);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                prop.WriteTo(writer);
            }
        }
        catch (JsonException)
        {
            // Skip invalid JSON
        }
    }

    private static void WriteStringOrNull(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
            writer.WriteNull(propertyName);
        else
            writer.WriteString(propertyName, value);
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.AsSpan().IndexOfAny([',', '"', '\n', '\r']) >= 0)
        {
            return string.Concat("\"", value.Replace("\"", "\"\""), "\"");
        }

        return value;
    }

    private static MemoryStream ToStream(StringBuilder sb)
    {
        var ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        ms.Position = 0;
        return ms;
    }
}
