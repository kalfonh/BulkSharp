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
