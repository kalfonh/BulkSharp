# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Common Commands

### Build and Test
```bash
# Build the entire solution
dotnet build

# Build specific configuration
dotnet build -c Release

# Run all tests
dotnet test

# Run tests with categories
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration
dotnet test --filter Category=E2E

# Run specific test project
dotnet test tests/BulkSharp.UnitTests/BulkSharp.UnitTests.csproj
```

### Development Setup
```bash
# Restore packages
dotnet restore

# Clean build artifacts
dotnet clean

# Run samples
dotnet run --project samples/BulkSharp.Sample.UserImport
dotnet run --project samples/BulkSharp.Sample.Dashboard
```

## Architecture Overview

BulkSharp is a production-grade .NET 8 library for bulk data processing operations. The architecture follows clean architecture principles with pluggable components.

### Core Project Structure

- **BulkSharp**: Meta-package entry point. Contains `BulkSharpBuilder`, DI registration (`AddBulkSharp()`), and assembly scanning for operations.
- **BulkSharp.Core**: Abstractions and domain models (`IBulkRowOperation`, `IBulkPipelineOperation`, `IBulkStep`, `IBulkMetadata`, `IBulkRow`)
- **BulkSharp.Processing**: Processing engine - processors, services, data format parsers, storage implementations, schedulers
- **BulkSharp.Dashboard**: Blazor Server dashboard for monitoring operations (Razor Class Library)
- **BulkSharp.Data.EntityFramework**: EF Core repositories for SQL Server persistence

### Key Abstractions

#### Bulk Operations
- `IBulkRowOperation<TMetadata, TRow>`: Main interface for defining bulk operations
- `IBulkPipelineOperation<TMetadata, TRow>`: For multi-step operations with `GetSteps()`
- `IBulkStep<TMetadata, TRow>`: Individual processing step with `Name`, `MaxRetries`, `ExecuteAsync()`
- Operations are decorated with `[BulkOperation("operation-name")]` attribute

#### Processing Pipeline
- `IBulkOperationService`: Main service for creating and managing bulk operations
- `IBulkOperationProcessor`: Processes individual operations
- `IBulkOperationProcessorFactory`: Generic dispatch via reflection
- `TypedBulkOperationProcessor<T, TMetadata, TRow>`: Typed processing with step detection
- `IBulkStepExecutor`: Executes step-based operations with retry and exponential backoff
- `IDataFormatProcessor<T>`: Handles CSV/JSON file parsing via `IAsyncEnumerable<T>`

#### Storage
- `IFileStorageProvider`: File storage abstraction (FileSystem, InMemory)
- `IManagedStorageProvider`: Combines file storage with metadata persistence
- `IBulkOperationRepository`: Operation persistence
- `IBulkRowRecordRepository`: Unified per-row tracking (validation, steps, errors, async completion)
- `IBulkFileRepository`: File metadata tracking

#### Events
- `IBulkOperationEventHandler`: Event hooks for operation lifecycle notifications
- `IBulkOperationEventDispatcher`: Internal event dispatch to registered handlers

#### Scheduling
- `IBulkScheduler`: Scheduling abstraction
- `ChannelsScheduler`: Production default using `System.Threading.Channels` (runs as `IHostedService`)
- `ImmediateScheduler`: Synchronous inline execution for testing

### Configuration (Builder Pattern)

The library uses `BulkSharpBuilder` with three configuration axes:

```csharp
services.AddBulkSharp(builder => builder
    .UseFileStorage(fs => fs.UseFileSystem(opts => opts.BasePath = "data/uploads"))  // or fs.UseInMemory()
    .UseMetadataStorage(ms => ms.UseSqlServer(opts => opts.ConnectionString = connectionString))  // or ms.UseInMemory(), ms.UseEntityFramework<TContext>()
    .UseScheduler(s => s.UseChannels(opts => opts.WorkerCount = 4)));  // or s.UseImmediate()

// Convenience methods:
services.AddBulkSharp();           // Defaults: file system + in-memory metadata + Channels scheduler
services.AddBulkSharpInMemory();   // All in-memory + immediate scheduler (testing)
services.AddBulkSharpDefaults();   // Explicit defaults
```

### Operation Implementation Pattern

```csharp
[BulkOperation("operation-name")]
public class MyOperation : IBulkRowOperation<MyMetadata, MyRow>
{
    Task ValidateMetadataAsync(MyMetadata metadata, CancellationToken cancellationToken);
    Task ValidateRowAsync(MyRow row, MyMetadata metadata, CancellationToken cancellationToken);
    Task ProcessRowAsync(MyRow row, MyMetadata metadata, CancellationToken cancellationToken);
}
```

Step-based operations add `GetSteps()`:

```csharp
[BulkOperation("step-operation")]
public class MyStepOperation : IBulkPipelineOperation<MyMetadata, MyRow>
{
    // ... same validation methods ...
    public IEnumerable<IBulkStep<MyMetadata, MyRow>> GetSteps()
    {
        yield return new Step1(); // MaxRetries = 3
        yield return new Step2(); // MaxRetries = 1
    }
}
```

### Service Registration Hierarchy

1. `AddBulkSharp()` creates `BulkSharpBuilder` and calls `Build()`
2. Builder registers file storage, metadata storage, scheduler (with defaults if not configured)
3. `RegisterProcessingServices()` registers processors, services, data format handlers
4. `RegisterBulkOperations()` scans assemblies for `[BulkOperation]`-decorated types

### Testing Structure

- **Unit Tests**: `tests/BulkSharp.UnitTests` - Uses xUnit, Moq, FluentAssertions
- **Integration Tests**: `tests/BulkSharp.IntegrationTests` - Full pipeline testing
- **E2E Tests**: `tests/BulkSharp.E2ETests` - Database integration with SQL Server
- **Dashboard Tests**: `tests/BulkSharp.Dashboard.Tests` - Blazor component testing

### Sample Projects

- **UserImport Sample**: Console app with regular and step-based operations
- **Dashboard Sample**: ASP.NET Core app with dashboard integration and example operations covering all features (CSV, JSON, attributes, steps, retries, error tracking)

## Important Notes

- The codebase uses .NET 8 with C# 12 language features
- Strict compilation settings: warnings treated as errors, nullable reference types enabled
- All storage and scheduling components are pluggable through DI
- The library supports both simple row-by-row operations and complex multi-step operations
- `[CsvColumn]` attributes are bridged to CsvHelper via `BulkSharpCsvClassMap<T>` at runtime
