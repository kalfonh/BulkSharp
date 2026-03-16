namespace BulkSharp.Core.Exceptions;

/// <summary>
/// Thrown when an external process signals that an async step has failed.
/// Contains the error message from the signaling process.
/// </summary>
public sealed class BulkStepSignalFailureException : BulkProcessingException
{
    public string SignalKey { get; }

    public BulkStepSignalFailureException(string signalKey, string errorMessage)
        : base(errorMessage)
    {
        SignalKey = signalKey;
    }

    public BulkStepSignalFailureException(string signalKey, string errorMessage, Exception innerException)
        : base(errorMessage, innerException)
    {
        SignalKey = signalKey;
    }
}
