
namespace BulkSharp.Core.Exceptions;

/// <summary>Thrown when a bulk operation encounters a non-recoverable processing error.</summary>
public class BulkProcessingException : Exception
{
    public BulkProcessingException(string message) : base(message) { }
    public BulkProcessingException(string message, Exception innerException) : base(message, innerException) { }
}
