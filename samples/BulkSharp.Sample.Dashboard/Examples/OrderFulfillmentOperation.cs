using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Abstractions.Processing;
using BulkSharp.Core.Attributes;
using BulkSharp.Core.Exceptions;

namespace BulkSharp.Sample.Dashboard.Examples;

/// <summary>
/// Step-based bulk operation demonstrating multi-step processing with different retry policies.
/// Each order goes through: Inventory Check (no retry) -> Payment Capture (3 retries) ->
/// Shipment Creation (2 retries) -> Customer Notification (1 retry).
///
/// This showcases:
///   - IBulkPipelineOperation with GetSteps()
///   - Different MaxRetries per step (0, 3, 2, 1)
///   - Per-row step execution with exponential backoff retry
///   - CSV with [CsvColumn] attributes for explicit column mapping
///   - [CsvSchema] attribute with version
///   - Cross-field validation (express shipping requires verified address)
/// </summary>
[BulkOperation("order-fulfillment",
    Description = "Multi-step order fulfillment pipeline. Each order goes through 4 steps with different " +
                  "retry policies: Inventory Check (no retry), Payment Capture (3 retries with exponential " +
                  "backoff), Shipment Creation (2 retries), and Customer Notification (1 retry). " +
                  "Demonstrates IBulkPipelineOperation with per-step retry configuration. " +
                  "CSV columns: OrderId, CustomerEmail, ProductSku, Quantity, UnitPrice, ShippingMethod, " +
                  "ShippingAddress, AddressVerified.")]
public class OrderFulfillmentOperation : IBulkPipelineOperation<OrderFulfillmentMetadata, OrderFulfillmentRow>
{
    public Task ValidateMetadataAsync(OrderFulfillmentMetadata metadata, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.WarehouseId))
            throw new BulkValidationException("WarehouseId is required - which warehouse fulfills these orders?");

        if (string.IsNullOrWhiteSpace(metadata.ProcessedBy))
            throw new BulkValidationException("ProcessedBy is required.");

        var validWarehouses = new[] { "WH-EAST", "WH-WEST", "WH-CENTRAL" };
        if (!validWarehouses.Contains(metadata.WarehouseId, StringComparer.OrdinalIgnoreCase))
            throw new BulkValidationException(
                $"Invalid WarehouseId '{metadata.WarehouseId}'. Must be one of: {string.Join(", ", validWarehouses)}");

        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(OrderFulfillmentRow row, OrderFulfillmentMetadata metadata, CancellationToken ct = default)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(row.OrderId))
            errors.Add("OrderId is required");

        if (string.IsNullOrWhiteSpace(row.CustomerEmail) || !row.CustomerEmail.Contains('@'))
            errors.Add($"Invalid CustomerEmail: '{row.CustomerEmail}'");

        if (string.IsNullOrWhiteSpace(row.ProductSku))
            errors.Add("ProductSku is required");

        if (row.Quantity <= 0)
            errors.Add("Quantity must be greater than zero");

        if (row.UnitPrice <= 0)
            errors.Add("UnitPrice must be greater than zero");

        var validMethods = new[] { "standard", "express", "overnight" };
        if (!validMethods.Contains(row.ShippingMethod, StringComparer.OrdinalIgnoreCase))
            errors.Add($"ShippingMethod must be standard, express, or overnight - got '{row.ShippingMethod}'");

        if (string.IsNullOrWhiteSpace(row.ShippingAddress))
            errors.Add("ShippingAddress is required");

        // Cross-field: express/overnight require verified address
        if (row.ShippingMethod?.Equals("standard", StringComparison.OrdinalIgnoreCase) != true
            && !row.AddressVerified)
            errors.Add($"Address must be verified for {row.ShippingMethod} shipping");

        if (errors.Count > 0)
            throw new BulkValidationException(
                $"Order {row.OrderId} validation failed: {string.Join("; ", errors)}");

        row.RowId ??= row.OrderId;

        return Task.CompletedTask;
    }

    public IEnumerable<IBulkStep<OrderFulfillmentMetadata, OrderFulfillmentRow>> GetSteps()
    {
        yield return new InventoryCheckStep();
        yield return new PaymentCaptureStep();
        yield return new ShipmentCreationStep();
        yield return new CustomerNotificationStep();
    }
}

// Step 1: Check inventory - no retry (if it's not in stock, it's not in stock)
public class InventoryCheckStep : IBulkStep<OrderFulfillmentMetadata, OrderFulfillmentRow>
{
    public string Name => "Inventory Check";
    public int MaxRetries => 0;

    public async Task ExecuteAsync(OrderFulfillmentRow row, OrderFulfillmentMetadata metadata, CancellationToken ct = default)
    {
        await Task.Delay(30, ct);

        // Simulate out-of-stock for quantity > 50
        if (row.Quantity > 50)
            throw new BulkProcessingException(
                $"Insufficient stock for SKU {row.ProductSku}: requested {row.Quantity}, available 50");

        Console.WriteLine($"[order-fulfillment] Inventory OK: {row.ProductSku} x{row.Quantity} at {metadata.WarehouseId}");
    }
}

// Step 2: Capture payment - 3 retries (payment gateways can be flaky)
public class PaymentCaptureStep : IBulkStep<OrderFulfillmentMetadata, OrderFulfillmentRow>
{
    public string Name => "Payment Capture";
    public int MaxRetries => 3;

    public async Task ExecuteAsync(OrderFulfillmentRow row, OrderFulfillmentMetadata metadata, CancellationToken ct = default)
    {
        await Task.Delay(80, ct);
        var total = row.Quantity * row.UnitPrice;
        Console.WriteLine($"[order-fulfillment] Payment captured: ${total:F2} for order {row.OrderId}");
    }
}

// Step 3: Create shipment - 2 retries (carrier API can timeout)
public class ShipmentCreationStep : IBulkStep<OrderFulfillmentMetadata, OrderFulfillmentRow>
{
    public string Name => "Shipment Creation";
    public int MaxRetries => 2;

    public async Task ExecuteAsync(OrderFulfillmentRow row, OrderFulfillmentMetadata metadata, CancellationToken ct = default)
    {
        await Task.Delay(60, ct);
        var trackingId = $"TRK-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        Console.WriteLine($"[order-fulfillment] Shipment created: {trackingId} via {row.ShippingMethod} from {metadata.WarehouseId}");
    }
}

// Step 4: Notify customer - 1 retry (email delivery is usually reliable)
public class CustomerNotificationStep : IBulkStep<OrderFulfillmentMetadata, OrderFulfillmentRow>
{
    public string Name => "Customer Notification";
    public int MaxRetries => 1;

    public async Task ExecuteAsync(OrderFulfillmentRow row, OrderFulfillmentMetadata metadata, CancellationToken ct = default)
    {
        await Task.Delay(20, ct);
        Console.WriteLine($"[order-fulfillment] Notification sent to {row.CustomerEmail} for order {row.OrderId}");
    }
}

public class OrderFulfillmentMetadata : IBulkMetadata
{
    public string WarehouseId { get; set; } = string.Empty;
    public string ProcessedBy { get; set; } = string.Empty;
    public bool PrioritizeExpress { get; set; } = true;
}

[CsvSchema("1.0")]
public class OrderFulfillmentRow : IBulkRow
{
    [CsvColumn("OrderId")]
    public string OrderId { get; set; } = string.Empty;

    [CsvColumn("CustomerEmail")]
    public string CustomerEmail { get; set; } = string.Empty;

    [CsvColumn("ProductSku")]
    public string ProductSku { get; set; } = string.Empty;

    [CsvColumn("Quantity")]
    public int Quantity { get; set; }

    [CsvColumn("UnitPrice")]
    public decimal UnitPrice { get; set; }

    [CsvColumn("ShippingMethod")]
    public string ShippingMethod { get; set; } = string.Empty;

    [CsvColumn("ShippingAddress")]
    public string ShippingAddress { get; set; } = string.Empty;

    [CsvColumn("AddressVerified")]
    public bool AddressVerified { get; set; }

    public string? RowId { get; set; }
}
