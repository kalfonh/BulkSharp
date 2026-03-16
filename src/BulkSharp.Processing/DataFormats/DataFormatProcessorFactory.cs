namespace BulkSharp.Processing.DataFormats;

internal sealed class DataFormatProcessorFactory<T>(IEnumerable<IDataFormatProcessor<T>> processors) :
    IDataFormatProcessorFactory<T> where T : class, new()
{
    private readonly IEnumerable<IDataFormatProcessor<T>> _processors = processors;

    public IEnumerable<string> SupportedFormats => _processors.Select(p => p.SupportedFormat);

    public IDataFormatProcessor<T> GetProcessor(string fileName)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.');
        var processor = _processors.FirstOrDefault(p => p.SupportedFormat.Equals(extension, StringComparison.OrdinalIgnoreCase));

        if (processor == null)
            throw new NotSupportedException($"No processor found for file: {fileName}");
        
        return processor;
    }
}
