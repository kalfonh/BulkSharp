using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Abstractions.Processing;
using BulkSharp.Core.Attributes;
using BulkSharp.Core.Exceptions;

namespace BulkSharp.Sample.Production.Operations;

/// <summary>
/// Regular bulk operation using JSON file format instead of CSV.
/// No [CsvColumn] or [CsvSchema] attributes - uses System.Text.Json property matching.
///
/// This showcases:
///   - JSON file format support (.json extension triggers JsonDataFormatProcessor)
///   - Auto-property mapping without attributes (property names match JSON keys)
///   - Simple row-by-row processing (no steps)
///   - Numeric validation and business rules
/// </summary>
[BulkOperation("inventory-update",
    Description = "Update product inventory levels from a JSON file. Each item specifies a SKU, " +
                  "warehouse location, quantity adjustment (positive for restock, negative for removal), " +
                  "and reason code. Demonstrates JSON file format support - no CSV attributes needed, " +
                  "properties are mapped directly from JSON keys. Validates SKU format, warehouse codes, " +
                  "and adjustment limits.")]
public class InventoryUpdateOperation : IBulkRowOperation<InventoryUpdateMetadata, InventoryUpdateRow>
{
    public Task ValidateMetadataAsync(InventoryUpdateMetadata metadata, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.ApprovedBy))
            throw new BulkValidationException("ApprovedBy is required for inventory adjustments.");

        if (string.IsNullOrWhiteSpace(metadata.AdjustmentBatchId))
            throw new BulkValidationException("AdjustmentBatchId is required for audit trail.");

        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(InventoryUpdateRow row, InventoryUpdateMetadata metadata, CancellationToken ct = default)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(row.Sku) || !row.Sku.StartsWith("SKU-"))
            errors.Add($"SKU must start with 'SKU-', got '{row.Sku}'");

        var validWarehouses = new[] { "WH-EAST", "WH-WEST", "WH-CENTRAL" };
        if (!validWarehouses.Contains(row.Warehouse, StringComparer.OrdinalIgnoreCase))
            errors.Add($"Invalid warehouse '{row.Warehouse}'");

        if (row.QuantityAdjustment == 0)
            errors.Add("QuantityAdjustment cannot be zero");

        if (Math.Abs(row.QuantityAdjustment) > 10000)
            errors.Add($"QuantityAdjustment exceeds limit of +/-10000, got {row.QuantityAdjustment}");

        var validReasons = new[] { "restock", "damaged", "return", "audit-correction", "transfer" };
        if (!validReasons.Contains(row.ReasonCode, StringComparer.OrdinalIgnoreCase))
            errors.Add($"Invalid ReasonCode '{row.ReasonCode}'. Must be one of: {string.Join(", ", validReasons)}");

        if (errors.Count > 0)
            throw new BulkValidationException($"SKU {row.Sku}: {string.Join("; ", errors)}");

        row.RowId ??= $"{row.Sku}/{row.Warehouse}";

        return Task.CompletedTask;
    }

    [BulkStep("Update Inventory")]
    public async Task ProcessRowAsync(InventoryUpdateRow row, InventoryUpdateMetadata metadata, CancellationToken ct = default)
    {
        await Task.Delay(40, ct);

        var direction = row.QuantityAdjustment > 0 ? "+" : "";
        Console.WriteLine(
            $"[inventory-update] {row.Warehouse} | {row.Sku} | {direction}{row.QuantityAdjustment} | " +
            $"reason: {row.ReasonCode} | batch: {metadata.AdjustmentBatchId}");
    }
}

public class InventoryUpdateMetadata : IBulkMetadata
{
    public string ApprovedBy { get; set; } = string.Empty;
    public string AdjustmentBatchId { get; set; } = string.Empty;
    public bool DryRun { get; set; } = false;
}

// No [CsvSchema] or [CsvColumn] attributes - JSON properties map directly
public class InventoryUpdateRow : IBulkRow
{
    public string Sku { get; set; } = string.Empty;
    public string Warehouse { get; set; } = string.Empty;
    public int QuantityAdjustment { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string? RowId { get; set; }
}
