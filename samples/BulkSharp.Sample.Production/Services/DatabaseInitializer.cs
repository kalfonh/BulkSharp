using BulkSharp.Data.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace BulkSharp.Sample.Production.Services;

public sealed class DatabaseInitializer(
    IServiceProvider serviceProvider,
    ILogger<DatabaseInitializer> logger,
    DatabaseReadySignal readySignal) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<BulkSharpDbContext>();

                logger.LogInformation("Ensuring database schema exists (attempt {Attempt})...", attempt);

                // EnsureCreatedAsync is a no-op if the database already exists, so new entities
                // added after initial creation won't get their tables. Delete + recreate for dev.
                await dbContext.Database.EnsureDeletedAsync(stoppingToken).ConfigureAwait(false);
                await dbContext.Database.EnsureCreatedAsync(stoppingToken).ConfigureAwait(false);
                logger.LogInformation("Database schema ready");
                readySignal.Signal();
                return;
            }
            catch (Exception ex) when (attempt < 10)
            {
                logger.LogWarning(ex, "Database not ready yet (attempt {Attempt}/30), retrying in 2s...", attempt);
                await Task.Delay(2000, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
