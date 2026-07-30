using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.Core.Contracts;

/// <summary>Execution state of one pipeline step for one row.</summary>
/// <param name="StepName">Name of the step.</param>
/// <param name="State">Current state of this step for this row.</param>
/// <param name="SignalKey">External signal key, present when the step waits for completion.</param>
/// <param name="StartedAt">When the step began, or null if not yet started.</param>
/// <param name="CompletedAt">When the step finished, or null if still in progress.</param>
/// <param name="ErrorMessage">Failure detail when the step failed.</param>
public sealed record RowStepDto(
    string StepName,
    RowRecordState State,
    string? SignalKey,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? ErrorMessage);
