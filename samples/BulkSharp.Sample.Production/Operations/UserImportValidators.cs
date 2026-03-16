using BulkSharp.Core.Abstractions.Processing;

namespace BulkSharp.Sample.Production.Operations;

/// <summary>
/// Validates that the department specified in metadata is from an allowed list.
/// Runs before the operation's own ValidateMetadataAsync.
/// Demonstrates IBulkMetadataValidator — a composable, DI-registered metadata validation hook.
/// </summary>
public sealed class DepartmentAllowlistValidator : IBulkMetadataValidator<UserImportMetadata>
{
    private static readonly HashSet<string> AllowedDepartments = new(StringComparer.OrdinalIgnoreCase)
    {
        "Engineering", "Product", "Marketing", "Sales", "Support", "HR", "Finance", "Operations"
    };

    public Task ValidateAsync(UserImportMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(metadata.Department) &&
            !AllowedDepartments.Contains(metadata.Department))
        {
            throw new ArgumentException(
                $"Department '{metadata.Department}' is not in the allowed list. " +
                $"Valid departments: {string.Join(", ", AllowedDepartments.Order())}",
                nameof(metadata));
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Validates that email addresses use an allowed corporate domain.
/// Runs before the operation's own ValidateRowAsync.
/// Demonstrates IBulkRowValidator — a composable, DI-registered row validation hook.
/// </summary>
public sealed class CorporateEmailValidator : IBulkRowValidator<UserImportMetadata, UserImportRow>
{
    private static readonly HashSet<string> AllowedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "example.com", "corp.example.com"
    };

    public Task ValidateAsync(UserImportRow row, UserImportMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(row.Email))
            return Task.CompletedTask;

        var domain = row.Email.Split('@', 2).ElementAtOrDefault(1);
        if (domain == null || !AllowedDomains.Contains(domain))
        {
            throw new ArgumentException(
                $"Email domain '{domain}' is not allowed. Must be one of: {string.Join(", ", AllowedDomains.Order())}");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Logs an audit trail after each row is processed.
/// Runs after the operation's own ProcessRowAsync.
/// Demonstrates IBulkRowProcessor — a composable, DI-registered post-processing hook.
/// </summary>
public sealed class UserImportAuditProcessor(ILogger<UserImportAuditProcessor> logger)
    : IBulkRowProcessor<UserImportMetadata, UserImportRow>
{
    public Task ProcessAsync(UserImportRow row, UserImportMetadata metadata, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[audit] User imported: {Email} to {Department} by {ImportedBy}",
            row.Email, row.Department, metadata.ImportedBy);

        return Task.CompletedTask;
    }
}
