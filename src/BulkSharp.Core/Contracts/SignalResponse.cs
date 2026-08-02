namespace BulkSharp.Core.Contracts;

/// <summary>Outcome of signalling a waiting pipeline step.</summary>
/// <param name="RowNumber">Row the signal was applied to.</param>
/// <param name="Step">Name of the step that was signalled.</param>
/// <param name="Completed">True when the step was marked complete.</param>
/// <param name="Failed">True when the step was marked failed.</param>
/// <param name="Error">Failure detail when <paramref name="Failed"/> is true.</param>
/// <param name="CrossProcess">
/// True when the signal was persisted for a worker in another process to pick up,
/// rather than delivered to a waiter in this process.
/// </param>
public sealed record SignalResponse(
    int RowNumber,
    string Step,
    bool Completed,
    bool Failed,
    string? Error,
    bool CrossProcess);
