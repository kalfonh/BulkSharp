using BulkSharp.Core.Configuration;
using BulkSharp.Processing.Storage;
using BulkSharp.Processing.Storage.InMemory;
using Microsoft.Extensions.Options;

namespace BulkSharp.UnitTests.Security;

[Trait("Category", "Unit")]
public class FileSizeLimitTests
{
    [Fact]
    public async Task StoreFileAsync_ExceedsMaxSize_ThrowsArgumentException()
    {
        var options = Options.Create(new BulkSharpOptions { MaxFileSizeBytes = 100 });
        var storageProvider = new InMemoryStorageProvider();
        var fileRepository = new InMemoryBulkFileRepository();
        var managed = new ManagedStorageProvider(storageProvider, fileRepository, options);

        var largeStream = new MemoryStream(new byte[200]);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            managed.StoreFileAsync(largeStream, "test.csv", "user"));
    }

    [Fact]
    public async Task StoreFileAsync_WithinLimit_Succeeds()
    {
        var options = Options.Create(new BulkSharpOptions { MaxFileSizeBytes = 1000 });
        var storageProvider = new InMemoryStorageProvider();
        var fileRepository = new InMemoryBulkFileRepository();
        var managed = new ManagedStorageProvider(storageProvider, fileRepository, options);

        var stream = new MemoryStream(new byte[100]);
        var result = await managed.StoreFileAsync(stream, "test.csv", "user");
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task StoreFileAsync_LimitDisabled_AcceptsAnySize()
    {
        var options = Options.Create(new BulkSharpOptions { MaxFileSizeBytes = 0 });
        var storageProvider = new InMemoryStorageProvider();
        var fileRepository = new InMemoryBulkFileRepository();
        var managed = new ManagedStorageProvider(storageProvider, fileRepository, options);

        var stream = new MemoryStream(new byte[1000]);
        var result = await managed.StoreFileAsync(stream, "test.csv", "user");
        Assert.NotEqual(Guid.Empty, result.Id);
    }
}
