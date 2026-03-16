using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Processing;
using BulkSharp.Core.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BulkSharp.ArchitectureTests;

/// <summary>
/// Verifies that the DI container can resolve all critical services
/// without runtime errors. Catches missing registrations, captive
/// dependencies, and lifetime mismatches at test time.
/// </summary>
[Trait("Category", "Architecture")]
public class DiRegistrationTests
{
    [Fact]
    public void InMemoryConfiguration_AllCriticalServices_CanBeResolved()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddBulkSharpInMemory())
            .Build();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IBulkOperationService>());
        Assert.NotNull(sp.GetRequiredService<IBulkOperationQueryService>());
        Assert.NotNull(sp.GetRequiredService<IBulkOperationProcessor>());
        Assert.NotNull(sp.GetRequiredService<IBulkOperationDiscovery>());
        Assert.NotNull(sp.GetRequiredService<IBulkScheduler>());
        Assert.NotNull(sp.GetRequiredService<IBulkOperationRepository>());
        Assert.NotNull(sp.GetRequiredService<IBulkFileRepository>());
        Assert.NotNull(sp.GetRequiredService<IBulkRowRecordRepository>());
        Assert.NotNull(sp.GetRequiredService<IFileStorageProvider>());
        Assert.NotNull(sp.GetRequiredService<IManagedStorageProvider>());
        Assert.NotNull(sp.GetRequiredService<IBulkStepSignalService>());
    }

    [Fact]
    public void DefaultConfiguration_AllCriticalServices_CanBeResolved()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddBulkSharpDefaults())
            .Build();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IBulkOperationService>());
        Assert.NotNull(sp.GetRequiredService<IBulkOperationProcessor>());
        Assert.NotNull(sp.GetRequiredService<IBulkScheduler>());
        Assert.NotNull(sp.GetRequiredService<IBulkOperationRepository>());
        Assert.NotNull(sp.GetRequiredService<IFileStorageProvider>());
        Assert.NotNull(sp.GetRequiredService<IManagedStorageProvider>());
    }

    [Fact]
    public void DoubleRegistration_DoesNotThrow()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddBulkSharpInMemory();
                services.AddBulkSharpInMemory(); // second call should be idempotent
            })
            .Build();

        using var scope = host.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBulkOperationService>());
    }

    [Fact]
    public void QueryService_ResolvesToSameInstanceAsOperationService()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddBulkSharpInMemory())
            .Build();

        using var scope = host.Services.CreateScope();
        var operationService = scope.ServiceProvider.GetRequiredService<IBulkOperationService>();
        var queryService = scope.ServiceProvider.GetRequiredService<IBulkOperationQueryService>();

        Assert.Same(operationService, queryService);
    }

    [Fact]
    public void ApiRegistration_ResolvesAllCriticalServices()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddBulkSharpApi(builder => builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory())))
            .Build();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IBulkOperationService>());
        Assert.NotNull(sp.GetRequiredService<IBulkOperationQueryService>());
        Assert.NotNull(sp.GetRequiredService<IBulkScheduler>());
        Assert.NotNull(sp.GetRequiredService<IBulkOperationRepository>());
        Assert.NotNull(sp.GetRequiredService<IBulkFileRepository>());
        Assert.NotNull(sp.GetRequiredService<IBulkRowRecordRepository>());
        Assert.NotNull(sp.GetRequiredService<IFileStorageProvider>());
        Assert.NotNull(sp.GetRequiredService<IManagedStorageProvider>());
        Assert.NotNull(sp.GetRequiredService<IBulkStepSignalService>());

        // Worker services must NOT be resolvable
        Assert.Null(sp.GetService<IBulkOperationProcessor>());
    }
}
