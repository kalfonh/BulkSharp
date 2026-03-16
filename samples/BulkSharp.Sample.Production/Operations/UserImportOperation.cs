using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Abstractions.Processing;
using BulkSharp.Core.Attributes;

namespace BulkSharp.Sample.Production.Operations;

[BulkOperation("user-import",
    Description = "Import users from a CSV file. Each row creates a new user account with name, email, department, and role. Validates email format and required fields before processing.")]
public class SampleUserImportOperation : IBulkRowOperation<UserImportMetadata, UserImportRow>
{
    public Task ValidateMetadataAsync(UserImportMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.ImportedBy))
            throw new ArgumentException("ImportedBy is required", nameof(metadata));

        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(UserImportRow row, UserImportMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.Email))
            throw new ArgumentException("Email is required", nameof(row));

        if (string.IsNullOrWhiteSpace(row.Name))
            throw new ArgumentException("Name is required", nameof(row));

        row.RowId ??= row.Email;

        return Task.CompletedTask;
    }

    [BulkStep("Import User")]
    public async Task ProcessRowAsync(UserImportRow row, UserImportMetadata metadata, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        Console.WriteLine($"[user-import] Created user: {row.Name} ({row.Email}) in {row.Department} - by {metadata.ImportedBy}");
    }
}

public class UserImportMetadata : IBulkMetadata
{
    public string ImportedBy { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public bool SendWelcomeEmail { get; set; } = true;
}

public class UserImportRow : IBulkRow
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? RowId { get; set; }
}
