using CsvHelper.Configuration;

namespace BulkSharp.Processing.DataFormats;

internal sealed class CsvDataFormatProcessor<T> : IDataFormatProcessor<T> where T : class, new()
{
    private static readonly Lazy<CsvSchemaAttribute?> SchemaAttribute =
        new(() => typeof(T).GetCustomAttribute<CsvSchemaAttribute>());

    private static readonly Lazy<string[]> RequiredColumnNames =
        new(() => typeof(T).GetProperties()
            .Select(p => p.GetCustomAttribute<CsvColumnAttribute>())
            .Where(attr => attr is { Required: true, Name: not null })
            .Select(attr => attr!.Name!)
            .ToArray());

    public string SupportedFormat => "csv";

    public async IAsyncEnumerable<T> ProcessAsync(Stream dataStream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(dataStream);
        using var csv = CreateCsvReader(reader);

        var hasHeader = SchemaAttribute.Value?.HasHeaderRecord ?? true;
        if (hasHeader)
        {
            await csv.ReadAsync().ConfigureAwait(false);
            csv.ReadHeader();
            ValidateRequiredColumns(csv.HeaderRecord);
        }
        else if (RequiredColumnNames.Value.Length > 0)
        {
            throw new BulkValidationException(
                "CSV schema has HasHeaderRecord=false but contains required columns by name. " +
                "Required column validation is not supported without headers.");
        }

        await foreach (var record in csv.GetRecordsAsync<T>(cancellationToken).ConfigureAwait(false))
        {
            yield return record;
        }
    }

    private static void ValidateRequiredColumns(string[]? headers)
    {
        var headerSet = new HashSet<string>(headers ?? [], StringComparer.OrdinalIgnoreCase);

        var missingColumns = RequiredColumnNames.Value
            .Where(name => !headerSet.Contains(name))
            .ToList();

        if (missingColumns.Count > 0)
        {
            throw new BulkValidationException(
                $"CSV is missing required column(s): {string.Join(", ", missingColumns)}");
        }
    }

    private static CsvReader CreateCsvReader(StreamReader reader)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture);

        var schemaAttr = SchemaAttribute.Value;
        if (schemaAttr != null)
        {
            config.Delimiter = schemaAttr.Delimiter;
            config.HasHeaderRecord = schemaAttr.HasHeaderRecord;
        }

        var csv = new CsvReader(reader, config);

        // Always register our ClassMap so that RowId (an infrastructure property
        // from IBulkRow) is ignored during CSV parsing. The map also bridges
        // BulkSharp's [CsvColumn] attributes when present.
        csv.Context.RegisterClassMap(new BulkSharpCsvClassMap<T>());

        return csv;
    }
}

/// <summary>
/// CsvHelper ClassMap that reads BulkSharp's [CsvColumn] attribute to map
/// CSV header names to C# property names.
/// </summary>
internal sealed class BulkSharpCsvClassMap<T> : ClassMap<T>
{
    public BulkSharpCsvClassMap()
    {
        foreach (var property in typeof(T).GetProperties())
        {
            // RowId is an infrastructure property set by the operation, not a CSV column
            if (property.Name == nameof(IBulkRow.RowId))
            {
                Map(typeof(T), property).Ignore();
                continue;
            }

            var csvColumn = property.GetCustomAttribute<CsvColumnAttribute>();
            var memberMap = Map(typeof(T), property);

            if (csvColumn?.Name != null)
            {
                memberMap.Name(csvColumn.Name);
            }

            if (csvColumn?.Index != null)
            {
                memberMap.Index(csvColumn.Index.Value);
            }

            if (csvColumn?.Format != null)
            {
                memberMap.TypeConverterOption.Format(csvColumn.Format);
            }

            if (csvColumn is { Required: false })
            {
                memberMap.Optional();
            }
        }
    }
}
