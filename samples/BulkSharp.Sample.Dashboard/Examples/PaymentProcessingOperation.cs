using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Abstractions.Processing;
using BulkSharp.Core.Attributes;
using BulkSharp.Core.Exceptions;

namespace BulkSharp.Sample.Dashboard.Examples;

/// <summary>
/// Regular bulk operation with [CsvSchema] and [CsvColumn] attributes demonstrating
/// explicit column mapping, custom column names (different from property names),
/// and intentional validation failures in sample data to show error tracking.
///
/// This showcases:
///   - [CsvSchema] with version and delimiter config
///   - [CsvColumn] with Name (mapping CSV header to property)
///   - Intentional errors in sample data (negative amounts, invalid currencies)
///     to demonstrate per-row error tracking in the dashboard
///   - Metadata validation with date range checks
///   - Currency and amount validation
/// </summary>
[BulkOperation("payment-processing",
    Description = "Process bulk payment transactions from CSV. Uses [CsvSchema] and [CsvColumn] attributes " +
                  "for explicit CSV-to-property mapping. Sample data includes intentional errors (negative " +
                  "amounts, invalid currencies) to demonstrate per-row error tracking in the dashboard. " +
                  "CSV columns: txn_id, payer_email, payee_email, amount_usd, currency, payment_method, reference_note.")]
public class PaymentProcessingOperation : IBulkRowOperation<PaymentProcessingMetadata, PaymentTransactionRow>
{
    public Task ValidateMetadataAsync(PaymentProcessingMetadata metadata, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.BatchApprover))
            throw new BulkValidationException("BatchApprover is required for payment batches.");

        if (metadata.SettlementDate == default)
            throw new BulkValidationException("SettlementDate is required.");

        if (metadata.SettlementDate.Date < DateTime.UtcNow.Date)
            throw new BulkValidationException(
                $"SettlementDate cannot be in the past, got {metadata.SettlementDate:yyyy-MM-dd}");

        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(PaymentTransactionRow row, PaymentProcessingMetadata metadata, CancellationToken ct = default)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(row.TransactionId))
            errors.Add("TransactionId is required");

        if (string.IsNullOrWhiteSpace(row.PayerEmail) || !row.PayerEmail.Contains('@'))
            errors.Add($"Invalid PayerEmail: '{row.PayerEmail}'");

        if (string.IsNullOrWhiteSpace(row.PayeeEmail) || !row.PayeeEmail.Contains('@'))
            errors.Add($"Invalid PayeeEmail: '{row.PayeeEmail}'");

        if (row.Amount <= 0)
            errors.Add($"Amount must be positive, got {row.Amount}");

        if (row.Amount > 100_000)
            errors.Add($"Amount exceeds single-transaction limit of $100,000, got {row.Amount}");

        var validCurrencies = new[] { "USD", "EUR", "GBP", "CAD" };
        if (!validCurrencies.Contains(row.Currency, StringComparer.OrdinalIgnoreCase))
            errors.Add($"Unsupported currency '{row.Currency}'. Must be one of: {string.Join(", ", validCurrencies)}");

        var validMethods = new[] { "ach", "wire", "check" };
        if (!validMethods.Contains(row.PaymentMethod, StringComparer.OrdinalIgnoreCase))
            errors.Add($"Invalid PaymentMethod '{row.PaymentMethod}'. Must be one of: {string.Join(", ", validMethods)}");

        if (row.PayerEmail == row.PayeeEmail)
            errors.Add("Payer and payee cannot be the same");

        if (errors.Count > 0)
            throw new BulkValidationException(
                $"Transaction {row.TransactionId}: {string.Join("; ", errors)}");

        row.RowId ??= row.TransactionId;

        return Task.CompletedTask;
    }

    [BulkStep("Process Payment")]
    public async Task ProcessRowAsync(PaymentTransactionRow row, PaymentProcessingMetadata metadata, CancellationToken ct = default)
    {
        await Task.Delay(100, ct);
        Console.WriteLine(
            $"[payment] {row.TransactionId}: {row.PayerEmail} -> {row.PayeeEmail} | " +
            $"${row.Amount:F2} {row.Currency} via {row.PaymentMethod} | settle: {metadata.SettlementDate:yyyy-MM-dd}");
    }
}

public class PaymentProcessingMetadata : IBulkMetadata
{
    public string BatchApprover { get; set; } = string.Empty;
    public DateTime SettlementDate { get; set; }
    public string Region { get; set; } = "US";
}

[CsvSchema("2.0")]
public class PaymentTransactionRow : IBulkRow
{
    [CsvColumn("txn_id")]
    public string TransactionId { get; set; } = string.Empty;

    [CsvColumn("payer_email")]
    public string PayerEmail { get; set; } = string.Empty;

    [CsvColumn("payee_email")]
    public string PayeeEmail { get; set; } = string.Empty;

    [CsvColumn("amount_usd")]
    public decimal Amount { get; set; }

    [CsvColumn("currency")]
    public string Currency { get; set; } = string.Empty;

    [CsvColumn("payment_method")]
    public string PaymentMethod { get; set; } = string.Empty;

    [CsvColumn("reference_note")]
    public string ReferenceNote { get; set; } = string.Empty;

    public string? RowId { get; set; }
}
