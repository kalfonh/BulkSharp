namespace BulkSharp.Core.Contracts;

/// <summary>Result of successfully submitting a bulk operation.</summary>
/// <param name="OperationId">Identifier of the newly created operation.</param>
public sealed record CreateOperationResponse(Guid OperationId);
