using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.Core.Contracts;

/// <summary>A single failed row within a bulk operation.</summary>
/// <param name="Id">Row record identifier.</param>
/// <param name="BulkOperationId">Owning operation identifier.</param>
/// <param name="RowNumber">One-based row position in the source file.</param>
/// <param name="RowId">Business identifier extracted from the row, when available.</param>
/// <param name="ErrorType">Classification of the failure, or null when unclassified.</param>
/// <param name="ErrorMessage">Human-readable failure detail.</param>
/// <param name="RowData">
/// Raw row payload. Present only when the operation enables row-data tracking.
/// </param>
/// <param name="CreatedAt">When the failure was recorded.</param>
public sealed record RowErrorDto(
    Guid Id,
    Guid BulkOperationId,
    int RowNumber,
    string? RowId,
    BulkErrorType? ErrorType,
    string? ErrorMessage,
    string? RowData,
    DateTime CreatedAt);
