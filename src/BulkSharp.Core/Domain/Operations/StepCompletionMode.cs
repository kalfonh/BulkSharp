namespace BulkSharp.Core.Domain.Operations;

/// <summary>
/// How an async step determines completion.
/// </summary>
public enum StepCompletionMode
{
    /// <summary>Step polls a condition until it returns true.</summary>
    Polling,

    /// <summary>Step waits for an external signal via API/webhook.</summary>
    Signal
}
