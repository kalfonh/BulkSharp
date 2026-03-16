namespace BulkSharp.Sample.UserImport.RegularBulk;

[BulkOperation("CreateUser")]
public class CreateUserBulkOperation : IBulkRowOperation<CreateUserMetadata, CreateUserRow>
{
    public Task ValidateMetadataAsync(
        CreateUserMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.RequestedBy))
            throw new BulkValidationException("RequestedBy is required.");

        if (metadata.EffectiveDate > DateTime.UtcNow.AddDays(30))
            throw new BulkValidationException("EffectiveDate cannot be more than 30 days in the future.");

        if (metadata.BatchSize <= 0 || metadata.BatchSize > 1000)
            throw new BulkValidationException("BatchSize must be between 1 and 1000.");

        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(
        CreateUserRow row,
        CreateUserMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.FirstName))
            throw new BulkValidationException("FirstName is required.");

        if (string.IsNullOrWhiteSpace(row.LastName))
            throw new BulkValidationException("LastName is required.");

        if (string.IsNullOrWhiteSpace(row.Email) || !IsValidEmail(row.Email))
            throw new BulkValidationException($"Invalid email '{row.Email}' for user '{row.FirstName} {row.LastName}'.");

        row.RowId ??= row.Email;

        return Task.CompletedTask;
    }

    [BulkStep("Create User")]
    public async Task ProcessRowAsync(
        CreateUserRow row,
        CreateUserMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        // Simulate user creation with some processing time
        await Task.Delay(100, cancellationToken);
        
        var userType = metadata.IsVIP ? "VIP" : "Regular";
        var status = metadata.EffectiveDate <= DateTime.UtcNow ? "Active" : "Pending";
        
        Console.WriteLine($"✅ Created {userType} user: {row.FirstName} {row.LastName} ({row.Email}) - Status: {status} - Dept: {metadata.Department}");
        
        // Simulate potential processing delay for VIP users
        if (metadata.IsVIP)
        {
            await Task.Delay(50, cancellationToken);
            Console.WriteLine($"⭐ VIP processing completed for {row.FirstName} {row.LastName}");
        }
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}
