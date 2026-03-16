# Pre-submission Validation

BulkSharp supports validating metadata and file structure before creating an operation. This allows clients to catch errors before committing to a full processing run.

## Programmatic Validation

```csharp
var result = await service.ValidateBulkOperationAsync(
    "import-users",
    metadataJson,
    fileStream,
    "users.csv");

if (!result.IsValid)
{
    foreach (var error in result.MetadataErrors)
        Console.WriteLine($"Metadata: {error}");

    foreach (var error in result.FileErrors)
        Console.WriteLine($"File: {error}");
}
```

## What Gets Validated

1. **Operation lookup** - Does an operation named `"import-users"` exist?
2. **Metadata deserialization** - Can the JSON be deserialized to the metadata type?
3. **Metadata validation** - Does `ValidateMetadataAsync` pass?
4. **File structure** - Can the file be parsed (CSV headers present, JSON valid)?
5. **Sample row validation** - Can the first row be read and validated?

Validation does **not** process all rows - it checks structure and the first row to catch format issues early.

## REST API

The dashboard exposes a validation endpoint:

```bash
POST /api/bulks/validate
Content-Type: multipart/form-data

operationName=import-users
metadata={"RequestedBy":"admin"}
file=@users.csv
```

Response:

```json
{
  "isValid": false,
  "metadataErrors": ["RequestedBy is required."],
  "fileErrors": []
}
```

## Dashboard Integration

The dashboard create form uses this endpoint with debounce for real-time validation. As users fill in the metadata fields and select a file, validation runs automatically and displays errors inline.

## Operation vs Composed: Two Validation Layers

BulkSharp validates at three levels — metadata, row, and operation lifecycle — each with two layers: **operation-level** (intrinsic rules defined on the operation class) and **composed** (cross-cutting rules registered as separate classes via DI). Both run automatically.

### Metadata Validation

**Operation-level:** `ValidateMetadataAsync` on the operation class. Defines rules that are fundamental to the operation — it cannot function without them.

```csharp
[BulkOperation("user-import")]
public class UserImportOperation : IBulkRowOperation<UserImportMetadata, UserImportRow>
{
    public Task ValidateMetadataAsync(UserImportMetadata metadata, CancellationToken ct = default)
    {
        // This operation cannot run without knowing who initiated the import
        if (string.IsNullOrWhiteSpace(metadata.ImportedBy))
            throw new ArgumentException("ImportedBy is required");
        return Task.CompletedTask;
    }
    // ...
}
```

**Composed:** `IBulkMetadataValidator<TMetadata>` — a separate class, auto-discovered. Adds policy-level constraints that apply across the environment, not just this operation.

```csharp
// Policy: restrict departments to an allowed list
// This is a deployment concern, not intrinsic to what "user import" means
public class DepartmentAllowlistValidator : IBulkMetadataValidator<UserImportMetadata>
{
    public Task ValidateAsync(UserImportMetadata metadata, CancellationToken ct = default)
    {
        if (!AllowedDepartments.Contains(metadata.Department))
            throw new ArgumentException($"Department '{metadata.Department}' is not allowed");
        return Task.CompletedTask;
    }
}
```

**Execution order:** Composed metadata validators run first, then the operation's `ValidateMetadataAsync`. If any throws, the operation is not started.

### Row Validation

**Operation-level:** `ValidateRowAsync` on the operation class. Defines per-row rules intrinsic to the operation.

```csharp
public Task ValidateRowAsync(UserImportRow row, UserImportMetadata metadata, CancellationToken ct = default)
{
    // A user must have an email — this is a data requirement, not a policy
    if (string.IsNullOrWhiteSpace(row.Email))
        throw new ArgumentException("Email is required");
    return Task.CompletedTask;
}
```

**Composed:** `IBulkRowValidator<TMetadata, TRow>` — a separate class, auto-discovered. Adds cross-cutting row-level policies.

```csharp
// Policy: email must be from a corporate domain
// This is an organizational constraint, not a data format requirement
public class CorporateEmailValidator : IBulkRowValidator<UserImportMetadata, UserImportRow>
{
    public Task ValidateAsync(UserImportRow row, UserImportMetadata metadata, CancellationToken ct = default)
    {
        var domain = row.Email.Split('@').LastOrDefault();
        if (domain != "example.com")
            throw new ArgumentException($"Email domain '{domain}' is not allowed");
        return Task.CompletedTask;
    }
}
```

**Execution order:** For each row, composed row validators run first, then the operation's `ValidateRowAsync`. A row that fails validation is recorded as an error and not processed.

### Row Post-Processing

After a row is successfully processed by the operation's `ProcessRowAsync`, composed post-processors run.

**Composed:** `IBulkRowProcessor<TMetadata, TRow>` — a separate class, auto-discovered. Runs after the operation processes each row.

```csharp
// Audit trail: logs every successfully imported user
// This is an infrastructure concern — the operation shouldn't need to know about audit logging
public class UserImportAuditProcessor(ILogger<UserImportAuditProcessor> logger)
    : IBulkRowProcessor<UserImportMetadata, UserImportRow>
{
    public Task ProcessAsync(UserImportRow row, UserImportMetadata metadata, CancellationToken ct = default)
    {
        logger.LogInformation("[audit] User imported: {Email} by {ImportedBy}", row.Email, metadata.ImportedBy);
        return Task.CompletedTask;
    }
}
```

There is no operation-level equivalent of `IBulkRowProcessor` — post-processing is always composed. The operation's `ProcessRowAsync` is the primary processing step; composed processors add supplementary behavior.

### Full Execution Order

```
1. Composed metadata validators (IBulkMetadataValidator<T>)
2. Operation's ValidateMetadataAsync
   ↓ (if both pass, processing starts)
For each row:
  3. Composed row validators (IBulkRowValidator<TM, TR>)
  4. Operation's ValidateRowAsync
     ↓ (if both pass, row is processed)
  5. Operation's ProcessRowAsync (or pipeline steps)
  6. Composed row processors (IBulkRowProcessor<TM, TR>)
```

### When to Use Each

| Question | Answer |
|---|---|
| Can the operation function without this rule? | **No** → operation-level validation |
| Could this rule apply to other operations with the same types? | **Yes** → composed validator |
| Is this rule environment-specific (differs per deployment)? | **Yes** → composed validator with injected config |
| Does this add behavior after processing (audit, notification)? | **Yes** → composed processor (`IBulkRowProcessor`) |
| Is this a data format/required field check? | **Yes** → operation-level validation |
| Is this a business constraint (allowlists, quotas, access control)? | **Yes** → composed validator |
