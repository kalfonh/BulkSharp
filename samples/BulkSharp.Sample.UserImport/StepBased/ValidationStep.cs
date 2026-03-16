namespace BulkSharp.Sample.UserImport.StepBased;

public class ValidationStep : IBulkStep<CreateUserMetadata, CreateUserRow>
{
    public string Name => "Enhanced Validation";
    public int MaxRetries => 0;

    public async Task ExecuteAsync(
        CreateUserRow row,
        CreateUserMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"🔍 Step 1: Validating user {row.FirstName} {row.LastName}...");
        
        await Task.Delay(50, cancellationToken);
        
        var errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(row.FirstName))
            errors.Add("FirstName is required");
            
        if (string.IsNullOrWhiteSpace(row.LastName))
            errors.Add("LastName is required");
            
        if (string.IsNullOrWhiteSpace(row.Email))
            errors.Add("Email is required");
        else if (!IsValidEmail(row.Email))
            errors.Add($"Invalid email format: {row.Email}");
        
        if (errors.Any())
        {
            var errorMessage = $"Validation failed for {row.FirstName} {row.LastName}: {string.Join(", ", errors)}";
            throw new BulkValidationException(errorMessage);
        }
        
        Console.WriteLine($"✅ Validation passed for {row.FirstName} {row.LastName}");
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
