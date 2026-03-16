namespace BulkSharp.Core.Abstractions.DataFormats;

/// <summary>Processes data in a specific format (CSV, JSON, etc.) as an async stream.</summary>
public interface IDataFormatProcessor<T> where T : class, new()
{
    string SupportedFormat { get; }

    IAsyncEnumerable<T> ProcessAsync(
        Stream dataStream,
        CancellationToken cancellationToken = default);
}