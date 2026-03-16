# Step-Based Operations

For workflows where each row needs multiple processing phases, use `IBulkPipelineOperation`. Each step executes in order with its own retry policy and exponential backoff.

## Defining Steps

```csharp
[BulkOperation("onboard-employees")]
public class EmployeeOnboarding : IBulkPipelineOperation<OnboardMetadata, EmployeeRow>
{
    public Task ValidateMetadataAsync(OnboardMetadata metadata, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task ValidateRowAsync(EmployeeRow row, OnboardMetadata metadata, CancellationToken ct = default)
        => Task.CompletedTask;

    public IEnumerable<IBulkStep<OnboardMetadata, EmployeeRow>> GetSteps()
    {
        yield return new CreateAdAccountStep();
        yield return new AssignEquipmentStep();
        yield return new SendWelcomeEmailStep();
    }
}
```

Each step implements `IBulkStep<TMetadata, TRow>`:

```csharp
public class CreateAdAccountStep : IBulkStep<OnboardMetadata, EmployeeRow>
{
    public string Name => "Create AD Account";
    public int MaxRetries => 3;

    public Task ExecuteAsync(EmployeeRow row, OnboardMetadata metadata, CancellationToken ct = default)
    {
        // Create the AD account
        // If this throws, BulkSharp retries up to MaxRetries times
        // with exponential backoff between attempts
    }
}
```

## Attribute-Based Steps

Instead of separate step classes, define steps as methods on the operation class using `[BulkStep]`:

```csharp
[BulkOperation("onboard-employees")]
public class EmployeeOnboarding : IBulkPipelineOperation<OnboardMetadata, EmployeeRow>
{
    public Task ValidateMetadataAsync(OnboardMetadata metadata, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task ValidateRowAsync(EmployeeRow row, OnboardMetadata metadata, CancellationToken ct = default)
        => Task.CompletedTask;

    // No GetSteps() override — steps are auto-discovered from [BulkStep] methods

    [BulkStep("Create AD Account", Order = 1, MaxRetries = 3)]
    public async Task CreateAdAccountAsync(EmployeeRow row, OnboardMetadata metadata, CancellationToken ct)
    {
        // Create the AD account
    }

    [BulkStep("Assign Equipment", Order = 2)]
    public async Task AssignEquipmentAsync(EmployeeRow row, OnboardMetadata metadata, CancellationToken ct)
    {
        // Assign equipment
    }

    [BulkStep("Send Welcome Email", Order = 3, MaxRetries = 1)]
    public async Task SendWelcomeEmailAsync(EmployeeRow row, OnboardMetadata metadata, CancellationToken ct)
    {
        // Send welcome email
    }
}
```

**Auto-discovery rules:**
- Method signature must be `(TRow, TMetadata, CancellationToken) -> Task`
- `Order` determines execution sequence (lower first)
- `MaxRetries` sets per-step retry count with exponential backoff (default: 0)
- Steps are discovered when `GetSteps()` returns empty (the default)

**When to use attributes vs classes:**
- **Attributes**: Simple sync steps where the logic lives naturally on the operation class. Less boilerplate.
- **Classes**: Async steps (polling/signal) that implement `IAsyncBulkStep`, or steps that need injected dependencies.

## Hybrid: Attributes + Async Classes

The framework automatically merges `[BulkStep]` methods with steps from `GetSteps()`. This lets you keep simple sync steps as inline methods while using class-based implementations for async steps (polling/signal):

```csharp
[BulkOperation("device-provisioning", TrackRowData = true)]
public class DeviceProvisioning : IBulkPipelineOperation<Metadata, Row>
{
    // ... validation methods ...

    // Return only async class-based steps — sync steps are auto-discovered from [BulkStep] methods
    public IEnumerable<IBulkStep<Metadata, Row>> GetSteps()
    {
        yield return new ProfilePushStep();       // IAsyncBulkStep — polling
        yield return new CarrierApprovalStep();   // IAsyncBulkStep — signal
    }

    [BulkStep("SIM Activation", Order = 1, MaxRetries = 2)]
    public async Task SimActivationAsync(Row row, Metadata metadata, CancellationToken ct)
    {
        // Inline step logic — auto-discovered and merged
    }

    [BulkStep("Network Registration", Order = 2)]
    public async Task NetworkRegistrationAsync(Row row, Metadata metadata, CancellationToken ct)
    {
        // Inline step logic — auto-discovered and merged
    }

    [BulkStep("Customer Notification", Order = 5, MaxRetries = 1)]
    public async Task NotifyAsync(Row row, Metadata metadata, CancellationToken ct)
    {
        // Inline step logic — auto-discovered and merged
    }
}
```

**Merge rules:**
- `GetSteps()` results come first, then discovered `[BulkStep]` methods are appended
- If a discovered method has the same name as an explicit step, it's skipped (explicit wins)
- Final order: explicit steps in `GetSteps()` yield order, then discovered steps ordered by `Order`

## Processing Flow

For each row:
1. `ValidateRowAsync` runs first
2. Steps execute in the order returned by `GetSteps()`
3. If a step throws, BulkSharp retries with exponential backoff up to `MaxRetries`
4. If a step exhausts retries, the row is recorded as failed and remaining steps are skipped

## Retry Behavior

- Retry delay doubles with each attempt (exponential backoff)
- Each step has its own independent retry count
- Retries are per-row, per-step - other rows are unaffected

## Step vs Simple Operations

| Aspect | `IBulkRowOperation` | `IBulkPipelineOperation` |
|--------|-------------------|---------------------------|
| Processing | Single `ProcessRowAsync` | Multiple ordered steps |
| Retry | No built-in retry | Per-step retry with backoff |
| Progress tracking | Row-level | Row + step-level |
| Use case | Simple transforms | Multi-phase workflows |

## Per-Row Step Status

BulkSharp tracks the status of each step for each row via `BulkRowRecord` entries (one per step, identified by `StepIndex`). The dashboard displays this as a drill-down view showing which steps completed, which are in progress, and which failed.

Step states: `Pending` -> `Running` -> `Completed` | `Failed` | `TimedOut`

For async steps that wait for external completion: `Running` -> `WaitingForCompletion` -> `Completed` | `Failed` | `TimedOut`

## Next Steps

- [Async Steps](async-steps.md) - Polling and signal-based step completion
- [Parallel Processing](parallel-processing.md) - Process multiple rows concurrently
