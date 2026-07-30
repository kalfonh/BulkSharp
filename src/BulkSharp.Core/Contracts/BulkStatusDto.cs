using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.Core.Contracts;

/// <summary>Lightweight progress snapshot for a bulk operation, suitable for polling.</summary>
/// <param name="Status">Current lifecycle status.</param>
/// <param name="ProcessedRows">Rows processed so far.</param>
/// <param name="TotalRows">Total rows detected in the source file.</param>
/// <param name="ErrorCount">Number of rows that failed.</param>
/// <param name="CompletedAt">Completion timestamp, or null while the operation is still running.</param>
/// <param name="Progress">Percentage complete in the range 0-100. Zero when the row count is unknown.</param>
public sealed record BulkStatusDto(
    BulkOperationStatus Status,
    int ProcessedRows,
    int TotalRows,
    int ErrorCount,
    DateTime? CompletedAt,
    double Progress);
