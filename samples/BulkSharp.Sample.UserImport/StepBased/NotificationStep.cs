namespace BulkSharp.Sample.UserImport.StepBased;

public class NotificationStep : IBulkStep<CreateUserMetadata, CreateUserRow>
{
    public string Name => "Notification";
    public int MaxRetries => 2;

    public async Task ExecuteAsync(
        CreateUserRow row, 
        CreateUserMetadata metadata, 
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"📧 Step 3: Sending notifications for {row.FirstName} {row.LastName}...");
        
        // Simulate notification processing time
        await Task.Delay(100, cancellationToken);
        
        // Send welcome email to user
        Console.WriteLine($"   📨 Welcome email sent to {row.Email}");
        
        // Send notification to requester
        Console.WriteLine($"   📨 Creation notification sent to {metadata.RequestedBy}");
        
        // Send department notification if VIP
        if (metadata.IsVIP)
        {
            await Task.Delay(50, cancellationToken);
            Console.WriteLine($"   📨 VIP notification sent to {metadata.Department} department");
        }
        
        // Simulate potential notification failure for retry demonstration
        if (row.Email.Contains("retry"))
        {
            throw new BulkProcessingException($"Simulated notification failure for {row.Email}");
        }
        
        Console.WriteLine($"✅ All notifications sent for {row.FirstName} {row.LastName}");
    }
}
