namespace BulkSharp.Core.Attributes;

/// <summary>Configures CSV parsing options like delimiter and header presence.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CsvSchemaAttribute : Attribute
{
    public string Version { get; set; } = "1.0";
    public bool HasHeaderRecord { get; set; } = true;
    public string Delimiter { get; set; } = ",";

    public CsvSchemaAttribute() { }
    public CsvSchemaAttribute(string version) => Version = version;
}
