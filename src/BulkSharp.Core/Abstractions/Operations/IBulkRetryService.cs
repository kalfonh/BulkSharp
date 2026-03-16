using BulkSharp.Core.Domain.Retry;

namespace BulkSharp.Core.Abstractions.Operations;

public interface IBulkRetryService
{
    Task<RetrySubmission> RetryFailedRowsAsync(Guid operationId, RetryRequest request, CancellationToken cancellationToken = default);
    Task<RetrySubmission> RetryRowAsync(Guid operationId, int rowNumber, CancellationToken cancellationToken = default);
    Task<RetryEligibility> CanRetryAsync(Guid operationId, CancellationToken cancellationToken = default);
}
