namespace BulkSharp.Core.Domain.Files;

/// <summary>File metadata record linking an uploaded file to its storage location.</summary>
public sealed class BulkFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OriginalFileName { get; set; } = string.Empty;
    public Guid StorageKey { get; set; }
    public string StorageProvider { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
    public bool IsDeleted { get; set; }
}
