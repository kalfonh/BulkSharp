namespace BulkSharp.Core.Attributes;

/// <summary>Maps a property to a CSV column by name or index position.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class CsvColumnAttribute : Attribute
{
    public string? Name { get; set; }
    public int? Index { get; set; }
    public bool Required { get; set; } = true;
    public string? Format { get; set; }

    public CsvColumnAttribute() { }
    public CsvColumnAttribute(string name) => Name = name;
    public CsvColumnAttribute(int index) => Index = index;
}
