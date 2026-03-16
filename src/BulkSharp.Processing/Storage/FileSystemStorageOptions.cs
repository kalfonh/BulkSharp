namespace BulkSharp.Processing.Storage;

/// <summary>
/// Configuration options for local file system storage.
/// </summary>
public sealed class FileSystemStorageOptions
{
    /// <summary>Base directory for stored files. Default: "bulksharp-storage".</summary>
    public string BasePath { get; set; } = "bulksharp-storage";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BasePath))
            throw new ArgumentException("BasePath is required.", nameof(BasePath));
    }
}
