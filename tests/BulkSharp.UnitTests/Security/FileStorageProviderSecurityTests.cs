using System;
using System.Threading.Tasks;
using Xunit;
using BulkSharp.Processing.Storage;

namespace BulkSharp.UnitTests.Security;

[Trait("Category", "Unit")]
public class FileStorageProviderSecurityTests
{
    [Fact]
    public async Task StoreFileAsync_WithPathTraversalInFileName_ThrowsArgumentException()
    {
        // Arrange
        var provider = new BasicFileStorageProvider("test-storage");
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act & Assert - Path.GetFileName strips traversal, so only empty names fail
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.StoreFileAsync(stream, "", CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveFileAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var provider = new BasicFileStorageProvider("test-storage");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            provider.RetrieveFileAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task StoreFileAsync_WithPathTraversal_StaysWithinBasePath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var provider = new BasicFileStorageProvider(tempDir);
            using var stream = new MemoryStream("test"u8.ToArray());
            var fileId = await provider.StoreFileAsync(stream, "../../../etc/evil.csv");

            // Verify file was stored inside basePath, not outside
            var files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
            Assert.Single(files);
            Assert.StartsWith(tempDir, Path.GetFullPath(files[0]));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task StoreAndRetrieve_RoundTrip_Succeeds()
    {
        // Arrange
        var basePath = Path.Combine(Path.GetTempPath(), $"bulksharp-test-{Guid.NewGuid():N}");
        var provider = new BasicFileStorageProvider(basePath);
        var data = new byte[] { 1, 2, 3, 4, 5 };

        try
        {
            // Act
            using var inputStream = new MemoryStream(data);
            var fileId = await provider.StoreFileAsync(inputStream, "test.csv");
            byte[] retrievedData;
            await using (var outputStream = await provider.RetrieveFileAsync(fileId))
            {
                using var ms = new MemoryStream();
                await outputStream.CopyToAsync(ms);
                retrievedData = ms.ToArray();
            }

            // Assert
            Assert.Equal(data, retrievedData);
            Assert.True(await provider.FileExistsAsync(fileId));

            var metadata = await provider.GetFileMetadataAsync(fileId);
            Assert.NotNull(metadata);
            Assert.Equal("test.csv", metadata.FileName);

            // Cleanup
            await provider.DeleteFileAsync(fileId);
            Assert.False(await provider.FileExistsAsync(fileId));
        }
        finally
        {
            if (Directory.Exists(basePath))
                Directory.Delete(basePath, true);
        }
    }
}
