var builder = Host.CreateApplicationBuilder(args);

// Register cross-cutting hooks (run before/after the operation's own logic)
builder.Services.AddScoped<IBulkRowValidator<CreateUserMetadata, CreateUserRow>, EmailDomainValidator>();
builder.Services.AddScoped<IBulkRowProcessor<CreateUserMetadata, CreateUserRow>, AuditLogProcessor>();

builder.Services.AddBulkSharpDefaults();

builder.Services.AddLogging(logging => 
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

var host = builder.Build();

// Start the host so hosted services (like schedulers) can run
await host.StartAsync();

var logger = host.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("🚀 BulkSharp Sample Application Started");

var operationService = host.Services.GetRequiredService<IBulkOperationService>();

// Demo 1: Regular bulk operation
await RunRegularBulkOperationDemo(operationService, logger);

// Demo 2: Step-based bulk operation
await RunStepBasedBulkOperationDemo(operationService, logger);

logger.LogInformation("✅ All samples completed successfully!");

await host.StopAsync();

static async Task RunRegularBulkOperationDemo(
    IBulkOperationService operationService,
    ILogger logger)
{
    logger.LogInformation("📝 Running Regular Bulk Operation Demo...");
    
    try
    {
        var csvContent = "FirstName,LastName,Email\n" +
                        "John,Doe,john.doe@example.com\n" +
                        "Jane,Smith,jane.smith@example.com\n" +
                        "Bob,Johnson,bob.johnson@example.com\n" +
                        "Alice,Williams,alice.williams@example.com";
        
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new CreateUserMetadata 
        { 
            IsVIP = true, 
            RequestedBy = "admin", 
            EffectiveDate = DateTime.UtcNow 
        };
        
        logger.LogInformation("Creating regular bulk operation...");
        var operationId = await operationService.CreateBulkOperationAsync(
            "CreateUser", csvStream, "users.csv", metadata, "admin");
        
        logger.LogInformation("Created bulk operation with ID: {OperationId}", operationId);
        
        // Monitor progress
        await MonitorOperation(operationService, operationId, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Error in regular bulk operation demo");
    }
}

static async Task RunStepBasedBulkOperationDemo(IBulkOperationService operationService, ILogger logger)
{
    logger.LogInformation("🔄 Running Step-Based Bulk Operation Demo...");
    
    try
    {
        var csvContent = "FirstName,LastName,Email\n" +
                        "Charlie,Brown,charlie.brown@example.com\n" +
                        "Diana,Prince,diana.prince@example.com";
        
        var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var metadata = new CreateUserMetadata 
        { 
            IsVIP = false, 
            RequestedBy = "system", 
            EffectiveDate = DateTime.UtcNow 
        };
        
        logger.LogInformation("Creating step-based bulk operation...");
        var operationId = await operationService.CreateBulkOperationAsync(
            "CreateUserStepBased", csvStream, "step-users.csv", metadata, "system");
        
        logger.LogInformation("Created step-based operation with ID: {OperationId}", operationId);
        
        // Monitor progress
        await MonitorOperation(operationService, operationId, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Error in step-based bulk operation demo");
    }
}

static async Task MonitorOperation(IBulkOperationService operationService, Guid operationId, ILogger logger)
{
    var maxAttempts = 10;
    var attempt = 0;
    
    while (attempt < maxAttempts)
    {
        await Task.Delay(1000); // Wait 1 second
        attempt++;
        
        var operation = await operationService.GetBulkOperationAsync(operationId);
        if (operation == null)
        {
            logger.LogWarning("⚠️ Operation not found");
            break;
        }
        
        logger.LogInformation("📊 Status: {Status}, Progress: {Processed}/{Total}", 
            operation.Status, operation.ProcessedRows, operation.TotalRows);
        
        if (operation.Status == BulkOperationStatus.Completed || 
            operation.Status == BulkOperationStatus.Failed)
        {
            if (operation.Status == BulkOperationStatus.Completed)
            {
                logger.LogInformation("✅ Operation completed successfully!");
            }
            else
            {
                logger.LogError("❌ Operation failed");
            }
            break;
        }
    }
    
    if (attempt >= maxAttempts)
    {
        logger.LogWarning("⏰ Monitoring timeout reached");
    }
}
