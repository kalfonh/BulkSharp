using System.Collections.Concurrent;
using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Processing;
using BulkSharp.Core.Attributes;
using BulkSharp.Core.Exceptions;

namespace BulkSharp.Sample.Production.Operations;

/// <summary>
/// End-to-end device provisioning pipeline demonstrating both [BulkStep] attribute-based steps
/// (inline methods) and class-based async steps (polling + signal), merged automatically by the framework.
///
/// Steps (ordered by [BulkStep] Order, then GetSteps() position):
///   1. SIM Activation       — [BulkStep] method, MaxRetries=2, ~3% permanent failure, ~10% transient
///   2. Network Registration  — [BulkStep] method, no retries, ~5% failure
///   3. Profile Push          — Async polling class (IAsyncBulkStep), polls every 2s, 20s timeout
///   4. Carrier Approval      — Async signal class (IAsyncBulkStep), waits for external signal, 45s timeout
///   5. Customer Notification — [BulkStep] method, MaxRetries=1, ~2% failure
///
/// The framework merges GetSteps() results with auto-discovered [BulkStep] methods.
/// Async steps come from GetSteps(); sync steps are discovered from attributes.
/// Steps with duplicate names are deduped (GetSteps() wins).
/// </summary>
[BulkOperation("device-provisioning",
    Description = "End-to-end device provisioning pipeline with SIM activation, network registration, polling-based profile push, signal-based carrier approval, and customer notification. Includes non-deterministic failures for testing.",
    TrackRowData = true)]
public class DeviceProvisioningOperation : IBulkPipelineOperation<DeviceProvisioningMetadata, DeviceProvisioningRow>
{
    private static readonly ConcurrentDictionary<string, int> SimAttempts = new();

    public Task ValidateMetadataAsync(DeviceProvisioningMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.RequestedBy))
            throw new BulkValidationException("RequestedBy is required");
        if (string.IsNullOrWhiteSpace(metadata.NetworkId))
            throw new BulkValidationException("NetworkId is required");
        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(DeviceProvisioningRow row, DeviceProvisioningMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.DeviceId))
            throw new BulkValidationException("DeviceId is required");
        if (string.IsNullOrWhiteSpace(row.Imei) || row.Imei.Length != 15)
            throw new BulkValidationException($"Device {row.DeviceId}: IMEI must be exactly 15 digits");
        if (string.IsNullOrWhiteSpace(row.Iccid) || row.Iccid.Length < 18)
            throw new BulkValidationException($"Device {row.DeviceId}: ICCID must be at least 18 characters");
        if (string.IsNullOrWhiteSpace(row.CustomerEmail) || !row.CustomerEmail.Contains('@'))
            throw new BulkValidationException($"Device {row.DeviceId}: Invalid customer email");

        var validPlans = new[] { "basic", "standard", "premium", "enterprise" };
        if (!validPlans.Contains(row.Plan, StringComparer.OrdinalIgnoreCase))
            throw new BulkValidationException($"Device {row.DeviceId}: Plan must be one of: {string.Join(", ", validPlans)}");

        var validRegions = new[] { "US-EAST", "US-WEST", "EU-WEST", "EU-CENTRAL", "APAC" };
        if (!validRegions.Contains(row.Region, StringComparer.OrdinalIgnoreCase))
            throw new BulkValidationException($"Device {row.DeviceId}: Region must be one of: {string.Join(", ", validRegions)}");

        row.RowId ??= row.DeviceId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns only the async class-based steps. The framework auto-discovers the [BulkStep]
    /// methods below and merges them in, deduplicating by name.
    /// </summary>
    public IEnumerable<IBulkStep<DeviceProvisioningMetadata, DeviceProvisioningRow>> GetSteps()
    {
        yield return new ProfilePushStep();
        yield return new CarrierApprovalStep();
    }

    // ── Sync steps via [BulkStep] attributes — auto-discovered and merged ──

    /// <summary>
    /// Step 1: SIM Activation — ~3% permanent failure, ~10% transient on first attempt (recovered by retry).
    /// </summary>
    [BulkStep("SIM Activation", Order = 1, MaxRetries = 2)]
    public async Task SimActivationAsync(DeviceProvisioningRow row, DeviceProvisioningMetadata metadata, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(50, 200), ct);

        var attempt = SimAttempts.AddOrUpdate(row.DeviceId, 1, (_, v) => v + 1);
        var hash = row.DeviceId.GetHashCode();

        if (Math.Abs(hash) % 33 == 0)
            throw new InvalidOperationException($"SIM activation failed for ICCID {row.Iccid}: carrier rejected — SIM locked");

        if (attempt == 1 && Math.Abs(hash) % 10 == 1)
            throw new InvalidOperationException($"SIM activation timeout for ICCID {row.Iccid} — retrying");
    }

    /// <summary>
    /// Step 2: Network Registration — ~5% failure, no retries.
    /// </summary>
    [BulkStep("Network Registration", Order = 2)]
    public async Task NetworkRegistrationAsync(DeviceProvisioningRow row, DeviceProvisioningMetadata metadata, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(100, 300), ct);

        var hash = (row.DeviceId + "network").GetHashCode();
        if (Math.Abs(hash) % 20 == 0)
            throw new InvalidOperationException($"Network registration failed for {row.DeviceId} in {row.Region}: capacity limit reached");
    }

    /// <summary>
    /// Step 5: Customer Notification — ~2% failure, 1 retry.
    /// </summary>
    [BulkStep("Customer Notification", Order = 5, MaxRetries = 1)]
    public async Task CustomerNotificationAsync(DeviceProvisioningRow row, DeviceProvisioningMetadata metadata, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(30, 100), ct);

        var hash = (row.DeviceId + "notify").GetHashCode();
        if (Math.Abs(hash) % 50 == 0)
            throw new InvalidOperationException($"Email delivery failed for {row.CustomerEmail}: SMTP timeout");
    }
}
