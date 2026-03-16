namespace BulkSharp.Core.Abstractions.DataFormats;

/// <summary>Selects the appropriate data format processor based on file extension.</summary>
public interface IDataFormatProcessorFactory<T> where T : class, new()
{
    IDataFormatProcessor<T> GetProcessor(string fileName);
    IEnumerable<string> SupportedFormats { get; }
}