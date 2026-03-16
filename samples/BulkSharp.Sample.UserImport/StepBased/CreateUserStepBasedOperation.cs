namespace BulkSharp.Sample.UserImport.StepBased;

[BulkOperation("CreateUserStepBased")]
public class CreateUserStepBasedOperation : IBulkPipelineOperation<CreateUserMetadata, CreateUserRow>
{
    public Task ValidateMetadataAsync(CreateUserMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.RequestedBy))
            throw new BulkValidationException("RequestedBy is required.");

        if (metadata.EffectiveDate > DateTime.UtcNow.AddDays(30))
            throw new BulkValidationException("EffectiveDate cannot be more than 30 days in the future.");

        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(CreateUserRow row, CreateUserMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(row.FirstName) || string.IsNullOrWhiteSpace(row.LastName))
            throw new BulkValidationException($"FirstName and LastName are required.");

        if (string.IsNullOrWhiteSpace(row.Email) || !IsValidEmail(row.Email))
            throw new BulkValidationException($"Invalid email '{row.Email}' for user '{row.FirstName} {row.LastName}'.");

        row.RowId ??= row.Email;

        return Task.CompletedTask;
    }

    public IEnumerable<IBulkStep<CreateUserMetadata, CreateUserRow>> GetSteps()
    {
        yield return new ValidationStep();
        yield return new UserCreationStep();
        yield return new NotificationStep();
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
