using BulkSharp.Core.Abstractions.Processing;
using BulkSharp.Core.Attributes;

namespace BulkSharp.Sample.Production.Operations;

[CsvSchema("1.0")]
public class DeviceProvisioningRow : IBulkRow
{
    [CsvColumn("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    [CsvColumn("imei")]
    public string Imei { get; set; } = string.Empty;

    [CsvColumn("iccid")]
    public string Iccid { get; set; } = string.Empty;

    [CsvColumn("customer_name")]
    public string CustomerName { get; set; } = string.Empty;

    [CsvColumn("customer_email")]
    public string CustomerEmail { get; set; } = string.Empty;

    [CsvColumn("plan")]
    public string Plan { get; set; } = string.Empty;

    [CsvColumn("region")]
    public string Region { get; set; } = string.Empty;

    [CsvColumn("priority")]
    public string Priority { get; set; } = string.Empty;

    public string? RowId { get; set; }
}
