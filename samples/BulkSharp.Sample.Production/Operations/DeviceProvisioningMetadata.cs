using BulkSharp.Core.Abstractions.Processing;

namespace BulkSharp.Sample.Production.Operations;

public class DeviceProvisioningMetadata : IBulkMetadata
{
    public string RequestedBy { get; set; } = string.Empty;
    public string NetworkId { get; set; } = string.Empty;
    public bool EnableVoLTE { get; set; }
    public bool EnableRoaming { get; set; }
}
