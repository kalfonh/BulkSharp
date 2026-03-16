namespace BulkSharp.Core.Domain.Files;

/// <summary>
/// Represents metadata for a file in bulk operations
/// </summary>
public sealed class BulkFileMetadata
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? ContentType { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ChecksumMD5 { get; set; }
}
