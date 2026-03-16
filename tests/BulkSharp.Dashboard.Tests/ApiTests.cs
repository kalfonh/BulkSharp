using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Domain.Operations;
using BulkSharp.Core.Domain.Queries;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BulkSharp.Dashboard.Tests;

[Trait("Category", "Integration")]
public class ApiTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddBulkSharp(b =>
        {
            b.UseFileStorage(fs => fs.UseInMemory())
             .UseMetadataStorage(ms => ms.UseInMemory())
             .UseScheduler(s => s.UseImmediate());
        });
        builder.Services.AddBulkSharpDashboard();

        _app = builder.Build();
        _app.UseBulkSharpDashboard();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task GetOperations_ReturnsOperationsList()
    {
        var response = await _client.GetAsync("/api/bulks");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PagedResult<BulkOperation>>();
        Assert.NotNull(result);
        Assert.NotNull(result.Items);
    }

    [Fact]
    public async Task GetOperation_WithValidId_ReturnsOperation()
    {
        // Create an operation via the repository
        using var scope = _app.Services.CreateScope();
        var operationRepo = scope.ServiceProvider.GetRequiredService<IBulkOperationRepository>();
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "test-operation",
            Status = BulkOperationStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        await operationRepo.CreateAsync(operation);

        var response = await _client.GetAsync($"/api/bulks/{operation.Id}");
        response.EnsureSuccessStatusCode();

        var returnedOperation = await response.Content.ReadFromJsonAsync<BulkOperation>();
        Assert.NotNull(returnedOperation);
        Assert.Equal(operation.Id, returnedOperation.Id);
    }

    [Fact]
    public async Task GetOperation_WithInvalidId_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/bulks/{Guid.NewGuid()}");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOperationErrors_ReturnsErrorsList()
    {
        var response = await _client.GetAsync($"/api/bulks/{Guid.NewGuid()}/errors");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("Items", out _) || json.TryGetProperty("items", out _));
    }
}
