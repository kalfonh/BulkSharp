using BulkSharp.Core.Domain.Events;

namespace BulkSharp.Core.Abstractions.Events;

public interface IBulkOperationEventDispatcher
{
    Task DispatchAsync(BulkOperationEvent e, CancellationToken ct = default);
}
