namespace BulkSharp.Core.Abstractions.Operations;

/// <summary>Processes a bulk operation by its ID. Entry point for the processing pipeline.</summary>
public interface IBulkOperationProcessor
{
    Task ProcessOperationAsync(Guid operationId, CancellationToken cancellationToken = default);
}