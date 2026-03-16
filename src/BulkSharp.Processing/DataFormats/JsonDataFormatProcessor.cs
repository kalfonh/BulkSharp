namespace BulkSharp.Processing.DataFormats;

internal sealed class JsonDataFormatProcessor<T> : IDataFormatProcessor<T> where T : class, new()
{
    public string SupportedFormat => "json";

    public async IAsyncEnumerable<T> ProcessAsync(Stream dataStream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var rowIndex = 0;
        await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<T>(dataStream, BulkSharpJsonDefaults.Options, cancellationToken).ConfigureAwait(false))
        {
            rowIndex++;
            if (item is null)
            {
                throw new BulkValidationException(
                    $"JSON array contains a null item at position {rowIndex}. All items must be non-null.");
            }

            yield return item;
        }
    }

}