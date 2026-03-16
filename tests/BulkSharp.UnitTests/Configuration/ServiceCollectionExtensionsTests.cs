using BulkSharp.Data.EntityFramework;
using BulkSharp.Processing.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BulkSharp.UnitTests;

[Trait("Category", "Unit")]
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBulkSharp_WithDefaultConfiguration_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBulkSharp();

        var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<IBulkOperationService>());
        Assert.NotNull(serviceProvider.GetService<IBulkOperationProcessor>());
    }

    [Fact]
    public void AddBulkSharp_WithBasicConfiguration_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBulkSharp(builder =>
        {
            builder.UseFileStorage(fs => fs.UseFileSystem());
            builder.UseScheduler(s => s.UseImmediate());
        });
        var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<IBulkOperationService>());
        Assert.NotNull(serviceProvider.GetService<IBulkOperationProcessor>());
    }

    [Fact]
    public void AddBulkSharp_WithInMemoryConfiguration_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBulkSharp(builder =>
        {
            builder.UseFileStorage(fs => fs.UseInMemory()).UseMetadataStorage(ms => ms.UseInMemory());
            builder.UseScheduler(s => s.UseImmediate());
        });

        var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<IBulkOperationService>());
        Assert.NotNull(serviceProvider.GetService<IBulkOperationProcessor>());
    }

    [Fact]
    public void AddBulkSharp_WithSqlServer_RegistersEntityFrameworkServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBulkSharp(builder =>
        {
            builder.UseScheduler(s => s.UseImmediate());
        });
        services.AddDbContext<BulkSharpDbContext>(options => options.UseInMemoryDatabase("TestDb"));
        services.AddBulkSharpEntityFramework<BulkSharpDbContext>();

        var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<BulkSharpDbContext>());
    }

    [Fact]
    public void AddBulkSharp_WithEntityFramework_RegistersAllServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBulkSharp(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory())
                .UseScheduler(s => s.UseImmediate());
        });
        services.AddDbContext<BulkSharpDbContext>(options => options.UseInMemoryDatabase("test"));
        services.AddBulkSharpEntityFramework<BulkSharpDbContext>();
        var serviceProvider = services.BuildServiceProvider();
        Assert.NotNull(serviceProvider.GetService<IBulkOperationService>());
        Assert.NotNull(serviceProvider.GetService<BulkSharpDbContext>());
    }

    [Fact]
    public void AddBulkSharpApi_RegistersApiServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBulkSharpApi(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory());
        });

        var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<IBulkOperationService>());
        Assert.NotNull(serviceProvider.GetService<IBulkOperationQueryService>());
        Assert.NotNull(serviceProvider.GetService<IBulkScheduler>());
        Assert.IsType<NullBulkScheduler>(serviceProvider.GetService<IBulkScheduler>());
        Assert.Null(serviceProvider.GetService<IBulkOperationProcessor>());
    }

    [Fact]
    public void AddBulkSharpApi_DoesNotStartHostedServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBulkSharpApi(builder =>
        {
            builder
                .UseFileStorage(fs => fs.UseInMemory())
                .UseMetadataStorage(ms => ms.UseInMemory());
        });

        var hostedServices = services
            .Where(s => s.ServiceType == typeof(IHostedService))
            .ToList();

        Assert.Empty(hostedServices);
    }

    [Fact]
    public void AddBulkSharpApi_DoubleRegistration_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBulkSharpApi();
        services.AddBulkSharpApi();

        var serviceProvider = services.BuildServiceProvider();
        var schedulers = serviceProvider.GetServices<IBulkScheduler>().ToList();

        Assert.Single(schedulers);
    }
}
