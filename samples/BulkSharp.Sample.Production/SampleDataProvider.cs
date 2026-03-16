namespace BulkSharp.Sample.Production;

public static class SampleDataProvider
{
    public record SampleInfo(
        string OperationName,
        string FileName,
        string FileContent,
        object Metadata,
        string Description,
        int RowCount);

    private static readonly Dictionary<string, SampleInfo> Samples = new()
    {
        ["user-import"] = new SampleInfo(
            "user-import",
            "users.csv",
            GetEmbeddedContent("users.csv"),
            new { ImportedBy = "admin@company.com", Department = "Engineering", SendWelcomeEmail = true },
            "Simple CSV user import with S3 file storage and SQL Server persistence.",
            8),

        ["order-processing"] = new SampleInfo(
            "order-processing",
            "orders.csv",
            GetEmbeddedContent("orders.csv"),
            new { WarehouseId = "WH-001", PaymentProvider = "stripe", ProcessingDate = DateTime.UtcNow.ToString("yyyy-MM-dd") },
            "Step-based order processing with async polling + signal steps and parallel row processing.",
            5),

        ["inventory-update"] = new SampleInfo(
            "inventory-update",
            "inventory.json",
            GetEmbeddedContent("inventory.json"),
            new { ApprovedBy = "warehouse-manager@company.com", AdjustmentBatchId = Guid.NewGuid().ToString(), DryRun = false },
            "JSON-format inventory adjustments demonstrating non-CSV file support.",
            5),

        ["device-provisioning"] = new SampleInfo(
            "device-provisioning",
            "devices.csv",
            GetEmbeddedContent("devices.csv"),
            new { RequestedBy = "ops-team@company.com", NetworkId = "NET-PROD-01", EnableVoLTE = true, EnableRoaming = false },
            "Complex device provisioning pipeline: SIM activation, network registration, OTA profile push (polling), carrier approval (signal), customer notification. 120 devices with ~15% expected failures across different steps.",
            120)
    };

    public static IReadOnlyDictionary<string, SampleInfo> GetAvailableSamples() => Samples;

    public static SampleInfo? GetSample(string operationName) =>
        Samples.GetValueOrDefault(operationName);

    private static string GetEmbeddedContent(string fileName)
    {
        var assembly = typeof(SampleDataProvider).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
            return string.Empty;

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
