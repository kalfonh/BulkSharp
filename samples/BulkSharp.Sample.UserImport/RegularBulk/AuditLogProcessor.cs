namespace BulkSharp.Sample.UserImport.RegularBulk;

/// <summary>
/// Sample IBulkRowProcessor that logs an audit trail after each row is processed.
/// Demonstrates how cross-cutting post-processing logic can be composed via DI
/// without modifying the operation itself. Runs after ProcessRowAsync (regular operations only).
/// </summary>
public class AuditLogProcessor(ILogger<AuditLogProcessor> logger) : IBulkRowProcessor<CreateUserMetadata, CreateUserRow>
{
    public Task ProcessAsync(CreateUserRow row, CreateUserMetadata metadata, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[Audit] User created: {Email}, Department: {Department}, RequestedBy: {RequestedBy}",
            row.Email, metadata.Department, metadata.RequestedBy);

        return Task.CompletedTask;
    }
}
