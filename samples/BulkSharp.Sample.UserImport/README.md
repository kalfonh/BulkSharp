# BulkSharp User Import Sample

Console application demonstrating BulkSharp with both regular and step-based bulk operations.

## Features Demonstrated

### Regular Bulk Operation (`RegularBulk/`)
- Simple row-by-row processing via `IBulkRowOperation<TMetadata, TRow>`
- Metadata and row validation
- Email format validation
- VIP user handling with enhanced processing

### Step-Based Bulk Operation (`StepBased/`)
- Multi-step processing via `IBulkPipelineOperation<TMetadata, TRow>`
- 3 steps with individual retry policies:
  - `ValidationStep` - Enhanced validation
  - `UserCreationStep` - User creation with failure simulation
  - `NotificationStep` - Notification with retry demonstration

## Project Structure

```
BulkSharp.Sample.UserImport/
  RegularBulk/
    CreateUserBulkOperation.cs    # IBulkRowOperation implementation
    CreateUserMetadata.cs         # Operation metadata
    CreateUserCsvRow.cs           # CSV row model
  StepBased/
    CreateUserStepBasedOperation.cs  # IBulkPipelineOperation implementation
    ValidationStep.cs               # Step 1: Enhanced validation
    UserCreationStep.cs             # Step 2: User creation
    NotificationStep.cs             # Step 3: Notifications
  sample-users.csv                # Sample CSV data
  Program.cs                      # Entry point
```

## Running

```bash
dotnet run --project samples/BulkSharp.Sample.UserImport
```

## Configuration

Uses default BulkSharp configuration:
- **Storage**: File system (`bulksharp-storage` directory)
- **Metadata**: In-memory repositories
- **Scheduler**: Channels-based background processing

## Related

- [Dashboard Sample](../BulkSharp.Sample.Dashboard/) - Web UI with example operations
- [Architecture](../../docs/guides/architecture.md)
- [Configuration](../../docs/guides/configuration.md)
