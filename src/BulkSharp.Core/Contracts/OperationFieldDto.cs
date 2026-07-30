namespace BulkSharp.Core.Contracts;

/// <summary>Describes one metadata property or file column of a bulk operation.</summary>
/// <param name="Name">The field name as it appears in metadata JSON or the file header.</param>
/// <param name="Type">
/// A friendly type name such as <c>string</c>, <c>int</c>, <c>int?</c> or <c>guid</c>.
/// A trailing <c>?</c> indicates a nullable type.
/// </param>
/// <param name="Required">Whether the field must be supplied.</param>
public sealed record OperationFieldDto(string Name, string Type, bool Required);
