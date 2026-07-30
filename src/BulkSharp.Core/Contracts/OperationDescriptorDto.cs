namespace BulkSharp.Core.Contracts;

/// <summary>
/// Describes a registered bulk operation type available for submission.
/// </summary>
/// <remarks>
/// This descriptor is what allows a client in any technology stack to render a
/// submission form without compile-time knowledge of the operation's types:
/// <see cref="MetadataFields"/> describes the metadata form, and
/// <see cref="FileColumns"/> describes the expected file header.
/// </remarks>
public sealed record OperationDescriptorDto
{
    /// <summary>The unique operation name used when creating a bulk operation.</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable description of what the operation does.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>True when the operation runs as a multi-step pipeline.</summary>
    public bool IsStepBased { get; init; }

    /// <summary>Simple name of the metadata type, or null when the operation has no metadata.</summary>
    public string? MetadataType { get; init; }

    /// <summary>Simple name of the row type.</summary>
    public string? RowType { get; init; }

    /// <summary>Full name of the operation implementation type.</summary>
    public string? TypeFullName { get; init; }

    /// <summary>
    /// The name of the backend service that owns this operation.
    /// Populated by the gateway when aggregating across services; null otherwise.
    /// </summary>
    public string? SourceService { get; init; }

    /// <summary>Writable metadata properties the caller may supply.</summary>
    public IReadOnlyList<OperationFieldDto> MetadataFields { get; init; } = [];

    /// <summary>Columns expected in the uploaded file.</summary>
    public IReadOnlyList<OperationFieldDto> FileColumns { get; init; } = [];
}
