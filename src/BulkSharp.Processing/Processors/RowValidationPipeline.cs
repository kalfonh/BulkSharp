namespace BulkSharp.Processing.Processors;

internal sealed class RowValidationPipeline<TMetadata, TRow>(
    IEnumerable<IBulkRowValidator<TMetadata, TRow>> rowValidators)
    : IRowValidationPipeline<TMetadata, TRow>
    where TMetadata : IBulkMetadata, new()
    where TRow : class, IBulkRow, new()
{
    private readonly IBulkRowValidator<TMetadata, TRow>[] _rowValidators = rowValidators.ToArray();

    public async Task<RowValidationError?> ValidateRowAsync(
        TRow row,
        TMetadata metadata,
        IBulkOperationBase<TMetadata, TRow> operation,
        int rowNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var validator in _rowValidators)
                await validator.ValidateAsync(row, metadata, cancellationToken).ConfigureAwait(false);

            await operation.ValidateRowAsync(row, metadata, cancellationToken).ConfigureAwait(false);

            return null;
        }
        catch (Exception ex)
        {
            var errorType = ex is BulkValidationException ? BulkErrorType.Validation : BulkErrorType.Processing;
            return new RowValidationError(ex.Message, errorType);
        }
    }
}
