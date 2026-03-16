namespace BulkSharp.Sample.UserImport.StepBased;

public class UserCreationStep : IBulkStep<CreateUserMetadata, CreateUserRow>
{
    public string Name => "User Creation";
    public int MaxRetries => 3;

    public async Task ExecuteAsync(
        CreateUserRow row,
        CreateUserMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"👤 Step 2: Creating user {row.FirstName} {row.LastName}...");
        
        // Simulate user creation processing time
        await Task.Delay(200, cancellationToken);
        
        var userType = metadata.IsVIP ? "VIP" : "Standard";
        var status = metadata.EffectiveDate <= DateTime.UtcNow ? "Active" : "Pending";
        
        // Simulate potential failure for demonstration (retry logic)
        if (row.FirstName.Contains("Fail"))
        {
            throw new BulkProcessingException($"Simulated failure creating user {row.FirstName} {row.LastName}");
        }
        
        Console.WriteLine($"✅ Created {userType} user: {row.FirstName} {row.LastName} ({row.Email})");
        Console.WriteLine($"   Status: {status} | Department: {metadata.Department} | Requested by: {metadata.RequestedBy}");
        
        // Additional processing for VIP users
        if (metadata.IsVIP)
        {
            await Task.Delay(100, cancellationToken);
            Console.WriteLine($"⭐ VIP privileges assigned to {row.FirstName} {row.LastName}");
        }
    }
}
