namespace BulkSharp.Core.Exceptions;

/// <summary>Thrown when metadata or row validation fails.</summary>
public sealed class BulkValidationException : BulkProcessingException
{
    public BulkValidationException(string message) : base(message) { }
    public BulkValidationException(string message, Exception innerException) : base(message, innerException) { }
}
