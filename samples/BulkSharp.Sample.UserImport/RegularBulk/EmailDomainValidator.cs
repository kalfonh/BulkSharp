namespace BulkSharp.Sample.UserImport.RegularBulk;

/// <summary>
/// Sample IBulkRowValidator that validates email domains against an allowed list.
/// Demonstrates how cross-cutting validation can be composed via DI
/// without modifying the operation itself.
/// </summary>
public class EmailDomainValidator : IBulkRowValidator<CreateUserMetadata, CreateUserRow>
{
    private static readonly HashSet<string> AllowedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "example.com",
        "company.com",
        "test.org"
    };

    public Task ValidateAsync(CreateUserRow row, CreateUserMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.Email))
            return Task.CompletedTask; // Let the operation's own validation handle this

        var domain = row.Email.Split('@', 2).LastOrDefault();
        if (domain != null && !AllowedDomains.Contains(domain))
        {
            throw new BulkValidationException(
                $"Email domain '{domain}' is not in the allowed list for user '{row.FirstName} {row.LastName}'.");
        }

        return Task.CompletedTask;
    }
}
