namespace BulkSharp.Core.Attributes;

/// <summary>
/// Marks an interface as a BulkSharp extensibility point.
/// Implementations are auto-discovered from scanned assemblies and registered in DI.
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class BulkExtensionPointAttribute : Attribute;
