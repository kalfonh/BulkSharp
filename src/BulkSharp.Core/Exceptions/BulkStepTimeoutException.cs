namespace BulkSharp.Core.Exceptions;

/// <summary>Thrown when an async bulk step exceeds its configured timeout waiting for completion.</summary>
public sealed class BulkStepTimeoutException : BulkProcessingException
{
    public BulkStepTimeoutException(string stepName, TimeSpan timeout)
        : base($"Async step '{stepName}' timed out after {timeout.TotalSeconds:F0}s waiting for completion") { }

    public BulkStepTimeoutException(string stepName, TimeSpan timeout, Exception innerException)
        : base($"Async step '{stepName}' timed out after {timeout.TotalSeconds:F0}s waiting for completion", innerException) { }
}
