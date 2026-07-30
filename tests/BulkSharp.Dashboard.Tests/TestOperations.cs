using System.ComponentModel.DataAnnotations;
using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Attributes;
using BulkSharp.Core.Domain.Processing;

namespace BulkSharp.Dashboard.Tests;

/// <summary>
/// Metadata for <see cref="ProbeOperation"/>, exercising a required field, an optional
/// field and a nullable value type so discovery output can be asserted meaningfully.
/// </summary>
public sealed class ProbeMetadata : BulkMetadata
{
    /// <summary>A required string field.</summary>
    [Required]
    public string AccountId { get; set; } = string.Empty;

    /// <summary>An optional string field.</summary>
    public string? Note { get; set; }

    /// <summary>An optional nullable integer field.</summary>
    public int? BatchSize { get; set; }
}

/// <summary>Row shape for <see cref="ProbeOperation"/>.</summary>
public sealed class ProbeRow : BulkRow
{
    /// <summary>A required column mapped to a differently-named CSV header.</summary>
    [CsvColumn("Email Address", Required = true)]
    public string Email { get; set; } = string.Empty;

    /// <summary>An optional column using the property name as the header.</summary>
    [CsvColumn(Required = false)]
    public string? DisplayName { get; set; }
}

/// <summary>
/// A discoverable no-op operation. Exists so the discovery endpoint returns a
/// deterministic descriptor to assert against rather than an empty list.
/// </summary>
[BulkOperation("probe-operation", Description = "Discovery probe used by the dashboard tests.")]
public sealed class ProbeOperation : IBulkRowOperation<ProbeMetadata, ProbeRow>
{
    /// <inheritdoc />
    public Task ValidateMetadataAsync(ProbeMetadata metadata, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task ValidateRowAsync(ProbeRow row, ProbeMetadata metadata, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task ProcessRowAsync(ProbeRow row, ProbeMetadata metadata, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
