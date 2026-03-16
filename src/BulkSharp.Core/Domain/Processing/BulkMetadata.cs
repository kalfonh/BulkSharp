namespace BulkSharp.Core.Domain.Processing;

/// <summary>
/// Base class for bulk operation metadata containing common properties
/// </summary>
public abstract class BulkMetadata : IBulkMetadata
{
    /// <summary>
    /// Gets or sets the operation identifier
    /// </summary>
    public Guid OperationId { get; set; }

    /// <summary>
    /// Gets or sets when the metadata was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets who created the bulk operation
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;
}