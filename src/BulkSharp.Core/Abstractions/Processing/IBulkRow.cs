namespace BulkSharp.Core.Abstractions.Processing;

/// <summary>Marker interface for bulk operation row types.</summary>
public interface IBulkRow
{
    /// <summary>
    /// Optional stable row identifier set by the operation (e.g., a business key from the data).
    /// Used for error correlation and step tracking. When null, RowNumber is used as fallback.
    /// </summary>
    string? RowId { get; set; }
}
