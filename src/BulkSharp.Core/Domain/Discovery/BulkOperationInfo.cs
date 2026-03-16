namespace BulkSharp.Core.Domain.Discovery;

/// <summary>Discovery metadata for a registered bulk operation type.</summary>
public sealed class BulkOperationInfo
{
    public string Name { get; init; } = string.Empty;
    public Type OperationType { get; init; } = null!;
    public Type MetadataType { get; init; } = null!;
    public Type RowType { get; init; } = null!;
    public bool IsStepBased { get; init; }
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Step name for simple operations, read from [BulkStep] on ProcessRowAsync.
    /// Falls back to the operation name if no attribute is present.
    /// </summary>
    public string DefaultStepName { get; init; } = string.Empty;
    public bool TrackRowData { get; init; }
}
