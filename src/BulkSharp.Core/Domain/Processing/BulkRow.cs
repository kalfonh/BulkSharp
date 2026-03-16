namespace BulkSharp.Core.Domain.Processing;

/// <summary>
/// Base class for bulk operation row data containing common properties
/// </summary>
public abstract class BulkRow : IBulkRow
{
    /// <summary>
    /// Gets or sets the row number in the source file
    /// </summary>
    public int RowNumber { get; set; }

    /// <inheritdoc />
    public string? RowId { get; set; }
}