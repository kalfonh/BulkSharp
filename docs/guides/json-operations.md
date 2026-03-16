# JSON Operations

BulkSharp processes JSON files using `System.Text.Json` with stream-based parsing.

## Row Definition

JSON rows map properties by name - no attributes needed:

```csharp
public class InventoryRow : IBulkRow
{
    public string Sku { get; set; } = string.Empty;
    public string Warehouse { get; set; } = string.Empty;
    public int QuantityAdjustment { get; set; }
    public string? RowId { get; set; }
}
```

## File Format

The expected JSON format is an array of objects:

```json
[
  { "Sku": "SKU-001", "Warehouse": "WH-EAST", "QuantityAdjustment": 100 },
  { "Sku": "SKU-002", "Warehouse": "WH-WEST", "QuantityAdjustment": -50 }
]
```

## Format Detection

BulkSharp detects the file format by extension:
- `.csv` files use the CSV parser (CsvHelper)
- `.json` files use the JSON parser (System.Text.Json)

The same operation class can process both formats if the row type works with both parsers.

## JSON vs CSV

| Aspect | CSV | JSON |
|--------|-----|------|
| Attributes | `[CsvColumn]` required | None needed |
| Header mapping | Explicit via attribute name | Property name (case-insensitive) |
| Nested objects | Not supported | Supported |
| Schema declaration | `[CsvSchema]` attribute | Not applicable |

## Property Naming

JSON deserialization uses case-insensitive property matching by default. Both `"sku"` and `"Sku"` will map to a `Sku` property.
