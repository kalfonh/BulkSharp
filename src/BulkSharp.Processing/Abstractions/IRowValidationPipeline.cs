namespace BulkSharp.Processing.Abstractions;

/// <summary>Carries validation failure details without coupling to a persistence model.</summary>
internal sealed record RowValidationError(string ErrorMessage, BulkErrorType ErrorType);

internal interface IRowValidationPipeline<TMetadata, TRow>
    where TMetadata : IBulkMetadata, new()
    where TRow : class, IBulkRow, new()
{
    Task<RowValidationError?> ValidateRowAsync(
        TRow row,
        TMetadata metadata,
        IBulkOperationBase<TMetadata, TRow> operation,
        int rowNumber,
        CancellationToken cancellationToken);
}
