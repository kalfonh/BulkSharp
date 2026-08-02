namespace BulkSharp.Core.Contracts;

/// <summary>Outcome of a pre-flight validation request.</summary>
/// <param name="Valid">True when the submission would be accepted.</param>
/// <param name="MetadataErrors">Validation failures against the metadata payload.</param>
/// <param name="FileErrors">Validation failures against the uploaded file.</param>
public sealed record ValidationResponse(
    bool Valid,
    IReadOnlyList<string> MetadataErrors,
    IReadOnlyList<string> FileErrors);
