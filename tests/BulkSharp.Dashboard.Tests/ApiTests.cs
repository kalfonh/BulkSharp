using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Contracts;
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

        var result = await response.Content.ReadFromJsonAsync<PagedResult<BulkOperation>>(
            BulkSharpJsonSerialization.Options);
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

        var returnedOperation = await response.Content.ReadFromJsonAsync<BulkOperation>(
            BulkSharpJsonSerialization.Options);
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
    public async Task GetOperations_ReturnsTypedDescriptors()
    {
        var descriptors = await _client.GetFromJsonAsync<List<OperationDescriptorDto>>(
            "/api/operations", BulkSharpJsonSerialization.Options);

        Assert.NotNull(descriptors);

        var probe = Assert.Single(descriptors, d => d.Name == "probe-operation");
        Assert.Equal("Discovery probe used by the dashboard tests.", probe.Description);
        Assert.False(probe.IsStepBased);
        Assert.Equal(nameof(ProbeMetadata), probe.MetadataType);
        Assert.Equal(nameof(ProbeRow), probe.RowType);
    }

    [Fact]
    public async Task GetOperations_DescribesMetadataFieldsWithTypesAndRequiredness()
    {
        var descriptors = await _client.GetFromJsonAsync<List<OperationDescriptorDto>>(
            "/api/operations", BulkSharpJsonSerialization.Options);

        var probe = Assert.Single(descriptors!, d => d.Name == "probe-operation");

        var accountId = Assert.Single(probe.MetadataFields, f => f.Name == nameof(ProbeMetadata.AccountId));
        Assert.Equal("string", accountId.Type);
        Assert.True(accountId.Required);

        var batchSize = Assert.Single(probe.MetadataFields, f => f.Name == nameof(ProbeMetadata.BatchSize));
        Assert.Equal("int?", batchSize.Type);
        Assert.False(batchSize.Required);
    }

    [Fact]
    public async Task GetOperations_DescribesFileColumnsUsingCsvColumnNames()
    {
        var descriptors = await _client.GetFromJsonAsync<List<OperationDescriptorDto>>(
            "/api/operations", BulkSharpJsonSerialization.Options);

        var probe = Assert.Single(descriptors!, d => d.Name == "probe-operation");

        var email = Assert.Single(probe.FileColumns, c => c.Name == "Email Address");
        Assert.Equal("string", email.Type);
        Assert.True(email.Required);

        var displayName = Assert.Single(probe.FileColumns, c => c.Name == nameof(ProbeRow.DisplayName));
        Assert.False(displayName.Required);
    }

    [Fact]
    public async Task GetStatus_ReturnsTypedStatusWithProgress()
    {
        using var scope = _app.Services.CreateScope();
        var operationRepo = scope.ServiceProvider.GetRequiredService<IBulkOperationRepository>();
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "status-probe",
            Status = BulkOperationStatus.Running,
            TotalRows = 4,
            ProcessedRows = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        await operationRepo.CreateAsync(operation);

        var status = await _client.GetFromJsonAsync<BulkStatusDto>(
            $"/api/bulks/{operation.Id}/status", BulkSharpJsonSerialization.Options);

        Assert.NotNull(status);
        Assert.Equal(BulkOperationStatus.Running, status.Status);
        Assert.Equal(1, status.ProcessedRows);
        Assert.Equal(4, status.TotalRows);
        Assert.Equal(25d, status.Progress);
        Assert.Null(status.CompletedAt);
    }

    [Fact]
    public async Task GetStatus_WithNoRows_ReportsZeroProgressRatherThanDividingByZero()
    {
        using var scope = _app.Services.CreateScope();
        var operationRepo = scope.ServiceProvider.GetRequiredService<IBulkOperationRepository>();
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "empty-probe",
            Status = BulkOperationStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        await operationRepo.CreateAsync(operation);

        var status = await _client.GetFromJsonAsync<BulkStatusDto>(
            $"/api/bulks/{operation.Id}/status", BulkSharpJsonSerialization.Options);

        Assert.NotNull(status);
        Assert.Equal(0d, status.Progress);
    }

    [Fact]
    public async Task GetStatus_WithUnknownId_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/bulks/{Guid.NewGuid()}/status");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Api_SerializesEnumsAsStrings()
    {
        using var scope = _app.Services.CreateScope();
        var operationRepo = scope.ServiceProvider.GetRequiredService<IBulkOperationRepository>();
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "enum-probe",
            Status = BulkOperationStatus.Running,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        await operationRepo.CreateAsync(operation);

        var json = await _client.GetStringAsync($"/api/bulks/{operation.Id}");

        Assert.Contains("\"status\":\"Running\"", json);
    }

    [Fact]
    public async Task Api_UsesCamelCasePropertyNames()
    {
        using var scope = _app.Services.CreateScope();
        var operationRepo = scope.ServiceProvider.GetRequiredService<IBulkOperationRepository>();
        var operation = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "casing-probe",
            Status = BulkOperationStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        await operationRepo.CreateAsync(operation);

        var json = await _client.GetStringAsync($"/api/bulks/{operation.Id}");

        Assert.Contains("\"operationName\":", json);
        Assert.DoesNotContain("\"OperationName\":", json);
    }

    [Fact]
    public async Task GetOperationErrors_ReturnsPagedTypedErrors()
    {
        var result = await _client.GetFromJsonAsync<PagedResult<RowErrorDto>>(
            $"/api/bulks/{Guid.NewGuid()}/errors", BulkSharpJsonSerialization.Options);

        Assert.NotNull(result);
        Assert.NotNull(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public async Task GetOperationErrors_UsesCamelCaseEnvelope()
    {
        var json = await _client.GetStringAsync($"/api/bulks/{Guid.NewGuid()}/errors");

        Assert.Contains("\"items\":", json);
        Assert.Contains("\"totalCount\":", json);
        Assert.DoesNotContain("\"Items\":", json);
    }
}
