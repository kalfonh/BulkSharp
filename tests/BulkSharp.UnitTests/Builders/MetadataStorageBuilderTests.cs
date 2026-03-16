using Microsoft.Extensions.DependencyInjection;
using BulkSharp.Processing.Storage.InMemory;

namespace BulkSharp.UnitTests.Builders;

[Trait("Category", "Unit")]
public class MetadataStorageBuilderTests
{
    [Fact]
    public void UseCustom_RegistersProvidedServices()
    {
        var services = new ServiceCollection();
        services.AddBulkSharp(b => b.UseMetadataStorage(ms =>
            ms.UseCustom(s =>
            {
                s.AddSingleton<IBulkOperationRepository, InMemoryBulkOperationRepository>();
                s.AddSingleton<IBulkFileRepository, InMemoryBulkFileRepository>();
                s.AddSingleton<IBulkRowRecordRepository, InMemoryBulkRowRecordRepository>();
            })));

        var provider = services.BuildServiceProvider();
        provider.GetService<IBulkOperationRepository>().Should().NotBeNull();
        provider.GetService<IBulkFileRepository>().Should().NotBeNull();
        provider.GetService<IBulkRowRecordRepository>().Should().NotBeNull();
    }
}
