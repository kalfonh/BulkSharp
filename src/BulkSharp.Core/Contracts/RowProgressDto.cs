using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.Core.Contracts;

/// <summary>Aggregated pipeline progress for a single row across all of its steps.</summary>
/// <param name="RowNumber">One-based row position in the source file.</param>
/// <param name="RowId">Business identifier extracted from the row, when available.</param>
/// <param name="CurrentStep">
/// The step the row is currently on, or the last step it reached.
/// Falls back to <c>Unknown</c> when no step information is available.
/// </param>
/// <param name="CurrentState">State of the row at <paramref name="CurrentStep"/>.</param>
/// <param name="CompletedSteps">Number of steps completed successfully.</param>
/// <param name="TotalSteps">Total number of execution steps for this row. Never less than one.</param>
/// <param name="Steps">Per-step detail, ordered by step index.</param>
public sealed record RowProgressDto(
    int RowNumber,
    string? RowId,
    string CurrentStep,
    RowRecordState CurrentState,
    int CompletedSteps,
    int TotalSteps,
    IReadOnlyList<RowStepDto> Steps);
