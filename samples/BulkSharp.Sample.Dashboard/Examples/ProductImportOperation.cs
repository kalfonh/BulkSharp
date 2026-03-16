using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Abstractions.Processing;
using BulkSharp.Core.Attributes;

namespace BulkSharp.Sample.Dashboard.Examples;

[BulkOperation("product-import",
    Description = "Bulk import products into the catalog. Accepts CSV with product name, description, price, category, and stock quantity. Validates that price is positive and required fields are present.")]
public class ProductImportOperation : IBulkRowOperation<ProductImportMetadata, ProductImportRow>
{
    public Task ValidateMetadataAsync(ProductImportMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.Store))
            throw new ArgumentException("Store is required", nameof(metadata));

        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(ProductImportRow row, ProductImportMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.Name))
            throw new ArgumentException("Product name is required", nameof(row));

        if (row.Price <= 0)
            throw new ArgumentException("Price must be greater than zero", nameof(row));

        row.RowId ??= row.Name;

        return Task.CompletedTask;
    }

    [BulkStep("Import Product")]
    public async Task ProcessRowAsync(ProductImportRow row, ProductImportMetadata metadata, CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken);
        Console.WriteLine($"[product-import] Added product: {row.Name} (${row.Price}) to store {metadata.Store}, category {row.Category}");
    }
}

public class ProductImportMetadata : IBulkMetadata
{
    public string Store { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool AutoGenerateSku { get; set; } = true;
}

public class ProductImportRow : IBulkRow
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public string? RowId { get; set; }
}
