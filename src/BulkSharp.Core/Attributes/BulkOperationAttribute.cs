namespace BulkSharp.Core.Attributes;

/// <summary>Marks a class as a bulk operation for automatic discovery and registration.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class BulkOperationAttribute(string operationName) : Attribute
{
    public string OperationName { get; } = operationName;
    public string Description { get; set; } = string.Empty;
    public bool TrackRowData { get; set; }
    public bool IsRetryable { get; set; }
}
