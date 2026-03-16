using System.Collections.Concurrent;
using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Domain.Operations;

namespace BulkSharp.Sample.Production.Operations;

/// <summary>
/// Step 3: Profile Push — Async polling step.
/// Simulates pushing a device profile and polling for OTA delivery confirmation.
/// Delivery takes 3-8 seconds (deterministic per device). Timeout at 20s.
/// </summary>
internal class ProfilePushStep : IAsyncBulkStep<DeviceProvisioningMetadata, DeviceProvisioningRow>
{
    private static readonly ConcurrentDictionary<string, DateTime> PushTimes = new();

    public string Name => "Profile Push";
    public int MaxRetries => 1;
    public StepCompletionMode CompletionMode => StepCompletionMode.Polling;
    public TimeSpan PollInterval => TimeSpan.FromSeconds(2);
    public TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public async Task ExecuteAsync(DeviceProvisioningRow row, DeviceProvisioningMetadata metadata, CancellationToken ct = default)
    {
        await Task.Delay(Random.Shared.Next(100, 250), ct);
        PushTimes[row.DeviceId] = DateTime.UtcNow;
    }

    public Task<bool> CheckCompletionAsync(DeviceProvisioningRow row, DeviceProvisioningMetadata metadata, CancellationToken ct = default)
    {
        if (!PushTimes.TryGetValue(row.DeviceId, out var pushTime))
            return Task.FromResult(false);

        var hash = Math.Abs((row.DeviceId + "profile").GetHashCode());
        var deliverySeconds = 3 + (hash % 6);
        var elapsed = (DateTime.UtcNow - pushTime).TotalSeconds;

        return Task.FromResult(elapsed >= deliverySeconds);
    }

    public string GetSignalKey(DeviceProvisioningRow row, DeviceProvisioningMetadata metadata) => string.Empty;
}

/// <summary>
/// Step 4: Carrier Approval — Async signal step.
/// Waits for external carrier system to approve via POST /api/bulks/{id}/signal/carrier-{deviceId}.
/// The DemoSignalService auto-approves after ~5 seconds. ~5% get signal-failed.
/// </summary>
internal class CarrierApprovalStep : IAsyncBulkStep<DeviceProvisioningMetadata, DeviceProvisioningRow>
{
    public string Name => "Carrier Approval";
    public int MaxRetries => 0;
    public StepCompletionMode CompletionMode => StepCompletionMode.Signal;
    public TimeSpan PollInterval => TimeSpan.Zero;
    public TimeSpan Timeout => TimeSpan.FromSeconds(45);

    public async Task ExecuteAsync(DeviceProvisioningRow row, DeviceProvisioningMetadata metadata, CancellationToken ct = default)
    {
        await Task.Delay(Random.Shared.Next(50, 150), ct);
    }

    public Task<bool> CheckCompletionAsync(DeviceProvisioningRow row, DeviceProvisioningMetadata metadata, CancellationToken ct = default)
        => Task.FromResult(false);

    public string GetSignalKey(DeviceProvisioningRow row, DeviceProvisioningMetadata metadata)
        => $"carrier-{row.DeviceId}";
}
