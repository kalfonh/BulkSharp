# Assembly Scanning and Discovery

BulkSharp automatically discovers operation types at startup by scanning assemblies for the `[BulkOperation]` attribute. This page covers how discovery works and how to control it.

## Default Behavior

When no assembly restrictions are configured, BulkSharp scans **all loaded assemblies** via `AppDomain.CurrentDomain.GetAssemblies()`:

```csharp
builder.Services.AddBulkSharp();
// Scans every loaded assembly for [BulkOperation]-decorated types
```

This is convenient for development but has drawbacks in larger applications:

- Scans framework and third-party assemblies unnecessarily
- Slower startup in applications with many loaded assemblies
- Harder to reason about which operations are registered

## Restricting to Specific Assemblies

Use `AddOperationsFromAssembly` or `AddOperationsFromAssemblyOf<T>` on the builder to limit scanning:

```csharp
builder.Services.AddBulkSharp(bulk => bulk
    .AddOperationsFromAssemblyOf<UserImportOperation>()
    .AddOperationsFromAssemblyOf<OrderExportOperation>()
);
```

Or by explicit assembly reference:

```csharp
builder.Services.AddBulkSharp(bulk => bulk
    .AddOperationsFromAssembly(typeof(UserImportOperation).Assembly)
);
```

When at least one assembly is specified, **only those assemblies are scanned**. The full-domain fallback is skipped entirely.

## How Discovery Works

### Registration Time (DI Setup)

During `AddBulkSharp`, the library calls `BulkOperationDiscoveryService.ScanAssemblies()` to find all `[BulkOperation]`-decorated types and register them as scoped services:

```csharp
// Internally:
foreach (var op in BulkOperationDiscoveryService.ScanAssemblies(assemblies))
    services.TryAddScoped(op.OperationType);
```

If assemblies were restricted, a `BulkOperationAssemblyScope` singleton is also registered so that runtime discovery uses the same assembly list.

### Runtime (Service Resolution)

`BulkOperationDiscoveryService` implements `IBulkOperationDiscovery` and is registered as a singleton. On first resolution it:

1. Scans the configured assemblies (or all loaded assemblies if no scope was set)
2. Validates that all operation names are unique (case-insensitive)
3. Builds an in-memory dictionary keyed by operation name

After initialization, `GetOperation(name)` and `DiscoverOperations()` are O(1) dictionary lookups.

### What Gets Discovered

A type is discovered when it meets **all** of these criteria:

- Decorated with `[BulkOperation("operation-name")]`
- Implements exactly one `IBulkOperationBase<TMetadata, TRow>` interface (either `IBulkRowOperation` or `IBulkPipelineOperation`)
- Loadable via reflection (types that throw `ReflectionTypeLoadException` are skipped gracefully)

Types that have the attribute but do not implement the interface are logged as warnings and skipped.

### Duplicate Name Detection

If two types share the same `[BulkOperation("name")]` value (case-insensitive), startup fails with an `InvalidOperationException` listing the conflicting types:

```
Duplicate BulkOperation names detected. Each operation must have a unique name.
Duplicates: 'user-import' is used by: MyApp.UserImportV1, MyApp.UserImportV2
```

## BulkOperationInfo

Each discovered operation produces a `BulkOperationInfo` record containing:

| Property | Description |
|----------|-------------|
| `Name` | The operation name from the attribute |
| `Description` | Optional description from the attribute |
| `OperationType` | The CLR type implementing the operation |
| `MetadataType` | The `TMetadata` generic argument |
| `RowType` | The `TRow` generic argument |
| `IsStepBased` | Whether the type implements `IBulkPipelineOperation` |
| `DefaultStepName` | For non-step-based operations, the `[BulkStep]` name on `ProcessRowAsync` if present |

## When to Restrict Assembly Scanning

**Restrict** when:

- Your application loads many assemblies (large modular apps, plugin systems)
- You want explicit control over which operations are available
- Startup time matters (e.g., serverless cold starts)
- You have multiple modules with `[BulkOperation]` types but only want a subset active

**Use the default** when:

- Small application with few assemblies
- All `[BulkOperation]` types should always be registered
- Development/prototyping where convenience matters more than precision

## Extension Point Auto-Discovery

In addition to operations, BulkSharp auto-discovers implementations of extensibility interfaces from the same scanned assemblies. Any interface in `BulkSharp.Core.Abstractions` marked with `[BulkExtensionPoint]` is eligible.

Currently auto-discovered:

| Interface | Purpose |
|---|---|
| `IBulkOperationEventHandler` | Lifecycle events (created, completed, failed) |
| `IBulkMetadataValidator<TMetadata>` | Validates operation metadata before processing |
| `IBulkRowValidator<TMetadata, TRow>` | Cross-cutting row validation before each row |
| `IBulkRowProcessor<TMetadata, TRow>` | Post-processing hook after each row |

Just implement the interface — no DI registration needed:

```csharp
public class AuditLogger : IBulkOperationEventHandler
{
    public async Task OnOperationCompletedAsync(BulkOperationCompletedEvent e, CancellationToken ct)
    {
        // Automatically discovered and registered
    }
}

public class EmailDomainValidator : IBulkRowValidator<UserImportMetadata, UserImportRow>
{
    public Task ValidateAsync(UserImportRow row, UserImportMetadata metadata, CancellationToken ct = default)
    {
        // Automatically discovered and registered
    }
}
```

### How It Works

The `[BulkExtensionPoint]` attribute on an interface marks it for auto-discovery. During `AddBulkSharp()`, after operations are registered, the library:

1. Collects all interfaces in the Core assembly decorated with `[BulkExtensionPoint]`
2. Scans the same assemblies used for operation discovery
3. Registers every concrete class implementing a discovered interface as a scoped service
4. Skips types already registered (e.g., via `AddEventHandler<T>()` or manual DI)

The `[BulkExtensionPoint]` attribute is intended for use on interfaces in `BulkSharp.Core.Abstractions` only. Consumer code does not use this attribute — it implements the marked interfaces instead.

### Backward Compatibility

Explicit registration still works. `builder.AddEventHandler<T>()` and manual `services.AddScoped<>()` calls are respected — auto-discovery skips already-registered types.

## Complete Example

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBulkSharp(bulk => bulk
    .AddOperationsFromAssemblyOf<UserImportOperation>()
    .AddOperationsFromAssemblyOf<InventoryUpdateOperation>()
    .UseFileStorage(fs => fs.UseFileSystem())
    .UseMetadataStorage(ms => ms.UseSqlServer(sql =>
        sql.ConnectionString = connectionString))
    .UseScheduler(s => s.UseChannels(opts => opts.WorkerCount = 4))
);
```

This configuration scans only the assemblies containing `UserImportOperation` and `InventoryUpdateOperation`, ignoring all other loaded assemblies.
