using BulkSharp.Processing.Storage.InMemory;

namespace BulkSharp.IntegrationTests;

[Trait("Category", "Integration")]
public class StorageIntegrationTests
{
    [Fact]
    public async Task InMemoryStorage_ShouldStoreAndRetrieveFiles()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        var content = "test,data,content\n1,2,3";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        
        // Act
        var fileId = await storage.StoreFileAsync(stream, "test.csv");
        var exists = await storage.FileExistsAsync(fileId);
        var retrievedStream = await storage.RetrieveFileAsync(fileId);
        
        // Assert
        Assert.True(exists);
        using var reader = new StreamReader(retrievedStream);
        var retrievedContent = await reader.ReadToEndAsync();
        Assert.Equal(content, retrievedContent);
    }
    
    [Fact]
    public async Task InMemoryStorage_ShouldDeleteFiles()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        var content = "test content";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        
        // Act
        var fileId = await storage.StoreFileAsync(stream, "test.csv");
        var existsBefore = await storage.FileExistsAsync(fileId);
        await storage.DeleteFileAsync(fileId);
        var existsAfter = await storage.FileExistsAsync(fileId);
        
        // Assert
        Assert.True(existsBefore);
        Assert.False(existsAfter);
    }
    
    [Fact]
    public async Task InMemoryStorage_NonExistentFile_ShouldThrowException()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        
        // Act & Assert
        var missingId = Guid.NewGuid();
        await Assert.ThrowsAsync<FileNotFoundException>(() => 
            storage.RetrieveFileAsync(missingId));
    }
}
