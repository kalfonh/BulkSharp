namespace BulkSharp.Core.Domain.Operations;

/// <summary>Result of pre-submission validation for a bulk operation (metadata + file structure).</summary>
public sealed class BulkValidationResult
{
    public bool IsValid => MetadataErrors.Count == 0 && FileErrors.Count == 0;
    public List<string> MetadataErrors { get; init; } = [];
    public List<string> FileErrors { get; init; } = [];
}
