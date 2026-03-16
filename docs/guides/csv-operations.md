# CSV Operations

BulkSharp processes CSV files using [CsvHelper](https://joshclose.github.io/CsvHelper/) with stream-based parsing via `IAsyncEnumerable<T>`.

## Row Definition

CSV rows require `[CsvColumn]` attributes for header mapping:

```csharp
[CsvSchema("1.0")]
public class UserRow : IBulkRow
{
    [CsvColumn("FirstName")]
    public string FirstName { get; set; } = string.Empty;

    [CsvColumn("LastName")]
    public string LastName { get; set; } = string.Empty;

    [CsvColumn("Email")]
    public string Email { get; set; } = string.Empty;
}
```

- `[CsvSchema("1.0")]` declares the CSV format version (for future schema evolution)
- `[CsvColumn("HeaderName")]` maps a CSV column header to a property
- Properties without `[CsvColumn]` are ignored during parsing

## Column Mapping

BulkSharp bridges `[CsvColumn]` to CsvHelper via `BulkSharpCsvClassMap<T>` at runtime. The attribute name must match the CSV header exactly (case-sensitive).

```csv
FirstName,LastName,Email
Alice,Johnson,alice@example.com
```

## The RowId Property

`IBulkRow` includes an optional `RowId` property:

```csharp
public string? RowId { get; set; }
```

If your CSV has a natural identifier (order number, SKU, etc.), map it to `RowId` for easier error tracking:

```csharp
[CsvColumn("OrderNumber")]
public string? RowId { get; set; }
```

## Type Conversion

CsvHelper handles standard .NET type conversions automatically: `string`, `int`, `decimal`, `DateTime`, `bool`, `enum`, etc. For custom types, register CsvHelper `TypeConverter`s in your DI container.
