using System.Collections.Concurrent;
using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Processing;
using BulkSharp.Core.Attributes;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Core.Exceptions;

namespace BulkSharp.Sample.Production.Operations;

/// <summary>
/// End-to-end order fulfillment pipeline exercising all step completion modes:
///   1. Inventory Check   - sync IBulkStep (simulated DB lookup)
///   2. Payment Capture   - IAsyncBulkStep with Polling (simulated gateway delay)
///   3. Shipment Creation - IAsyncBulkStep with Signal (awaits external webhook)
///   4. Customer Notification - sync IBulkStep (simulated email send)
/// </summary>
[BulkOperation("order-processing",
    Description = "End-to-end order fulfillment: inventory check (DB), payment capture (polling), " +
                  "shipment creation (signal), customer notification")]
public class OrderProcessingOperation : IBulkPipelineOperation<OrderProcessingMetadata, OrderProcessingRow>
{
    public Task ValidateMetadataAsync(OrderProcessingMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.WarehouseId))
            throw new BulkValidationException("WarehouseId is required");
        if (metadata.PaymentProvider is not ("stripe" or "paypal" or "square"))
            throw new BulkValidationException("PaymentProvider must be stripe, paypal, or square");
        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(OrderProcessingRow row, OrderProcessingMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (row.Quantity <= 0)
            throw new BulkValidationException($"Order {row.OrderId}: quantity must be positive");
        if (row.UnitPrice <= 0)
            throw new BulkValidationException($"Order {row.OrderId}: unit price must be positive");
        if (!row.CustomerEmail.Contains('@'))
            throw new BulkValidationException($"Order {row.OrderId}: invalid email");

        row.RowId ??= row.OrderId;

        return Task.CompletedTask;
    }

    public IEnumerable<IBulkStep<OrderProcessingMetadata, OrderProcessingRow>> GetSteps()
    {
        yield return new OrderProcessingInventoryCheckStep();
        yield return new OrderProcessingPaymentCaptureStep();
        yield return new OrderProcessingShipmentCreationStep();
        yield return new OrderProcessingCustomerNotificationStep();
    }
}

// Step 1: Sync DB lookup - check inventory availability
internal class OrderProcessingInventoryCheckStep : IBulkStep<OrderProcessingMetadata, OrderProcessingRow>
{
    public string Name => "Inventory Check";
    public int MaxRetries => 2;

    public async Task ExecuteAsync(OrderProcessingRow row, OrderProcessingMetadata metadata, CancellationToken cancellationToken = default)
    {
        // Simulate DB query
        await Task.Delay(200, cancellationToken);
        Console.WriteLine($"[OrderProcessing] Inventory OK: {row.ProductSku} x{row.Quantity} at {metadata.WarehouseId}");
    }
}

// Step 2: Async polling - initiate payment capture then poll for completion
internal class OrderProcessingPaymentCaptureStep : IAsyncBulkStep<OrderProcessingMetadata, OrderProcessingRow>
{
    private static readonly ConcurrentDictionary<string, DateTime> InitiationTimes = new();

    public string Name => "Payment Capture";
    public int MaxRetries => 1;
    public StepCompletionMode CompletionMode => StepCompletionMode.Polling;
    public TimeSpan PollInterval => TimeSpan.FromSeconds(2);
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public async Task ExecuteAsync(OrderProcessingRow row, OrderProcessingMetadata metadata, CancellationToken cancellationToken = default)
    {
        // Simulate payment initiation
        await Task.Delay(300, cancellationToken);
        InitiationTimes[row.OrderId] = DateTime.UtcNow;
        var total = row.Quantity * row.UnitPrice;
        Console.WriteLine($"[OrderProcessing] Payment initiated: ${total:F2} for order {row.OrderId} via {metadata.PaymentProvider}");
    }

    public Task<bool> CheckCompletionAsync(OrderProcessingRow row, OrderProcessingMetadata metadata, CancellationToken cancellationToken = default)
    {
        // Simulate payment processing delay - completes after 4 seconds
        if (InitiationTimes.TryGetValue(row.OrderId, out var initiated))
        {
            var elapsed = DateTime.UtcNow - initiated;
            return Task.FromResult(elapsed.TotalSeconds > 4);
        }

        return Task.FromResult(false);
    }

    public string GetSignalKey(OrderProcessingRow row, OrderProcessingMetadata metadata) => string.Empty;
}

// Step 3: Async signal - create shipment and wait for external confirmation
internal class OrderProcessingShipmentCreationStep : IAsyncBulkStep<OrderProcessingMetadata, OrderProcessingRow>
{
    public string Name => "Shipment Creation";
    public int MaxRetries => 0;
    public StepCompletionMode CompletionMode => StepCompletionMode.Signal;
    public TimeSpan PollInterval => TimeSpan.Zero;
    public TimeSpan Timeout => TimeSpan.FromSeconds(60);

    public async Task ExecuteAsync(OrderProcessingRow row, OrderProcessingMetadata metadata, CancellationToken cancellationToken = default)
    {
        // Simulate carrier API call
        await Task.Delay(200, cancellationToken);
        Console.WriteLine($"[OrderProcessing] Shipment requested for order {row.OrderId} to {row.ShippingAddress}");
    }

    public Task<bool> CheckCompletionAsync(OrderProcessingRow row, OrderProcessingMetadata metadata, CancellationToken cancellationToken = default)
    {
        // Not used for signal mode
        return Task.FromResult(false);
    }

    public string GetSignalKey(OrderProcessingRow row, OrderProcessingMetadata metadata) => $"shipment-{row.OrderId}";
}

// Step 4: Sync - send customer notification email
internal class OrderProcessingCustomerNotificationStep : IBulkStep<OrderProcessingMetadata, OrderProcessingRow>
{
    public string Name => "Customer Notification";
    public int MaxRetries => 1;

    public async Task ExecuteAsync(OrderProcessingRow row, OrderProcessingMetadata metadata, CancellationToken cancellationToken = default)
    {
        // Simulate email send
        await Task.Delay(100, cancellationToken);
        Console.WriteLine($"[OrderProcessing] Notification sent to {row.CustomerEmail} for order {row.OrderId}");
    }
}

public class OrderProcessingMetadata : IBulkMetadata
{
    public string WarehouseId { get; set; } = string.Empty;
    public string PaymentProvider { get; set; } = "stripe";
    public DateTime ProcessingDate { get; set; } = DateTime.UtcNow;
}

public class OrderProcessingRow : IBulkRow
{
    [CsvColumn("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [CsvColumn("customer_email")]
    public string CustomerEmail { get; set; } = string.Empty;

    [CsvColumn("product_sku")]
    public string ProductSku { get; set; } = string.Empty;

    [CsvColumn("quantity")]
    public int Quantity { get; set; }

    [CsvColumn("unit_price")]
    public decimal UnitPrice { get; set; }

    [CsvColumn("shipping_address")]
    public string ShippingAddress { get; set; } = string.Empty;

    public string? RowId { get; set; }
}
