using BulkSharp.Core.Domain.Operations;
using BulkSharp.Processing.Models;
using Index = BulkSharp.Dashboard.Pages.Index;
using BulkSharp.Dashboard.Pages;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace BulkSharp.Dashboard.Tests;

[Trait("Category", "Integration")]
public class BlazorComponentTests : TestContext
{
    private HttpClient CreateMockHttpClient(params (HttpStatusCode Status, HttpContent Content)[] responses)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        var setup = mockHandler.Protected()
            .SetupSequence<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());

        foreach (var (status, content) in responses)
        {
            setup.ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = status,
                Content = content
            });
        }

        return new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("http://localhost")
        };
    }

    [Fact]
    public void Index_RendersCorrectly()
    {
        // Index makes HTTP calls in OnInitializedAsync, mock both endpoints
        var httpClient = CreateMockHttpClient(
            (HttpStatusCode.OK, JsonContent.Create(Array.Empty<object>())),
            (HttpStatusCode.OK, JsonContent.Create(Array.Empty<object>()))
        );
        Services.AddSingleton(httpClient);

        var component = RenderComponent<Index>();

        Assert.Contains("BulkSharp Dashboard", component.Markup);
        Assert.Contains("Manage and monitor your bulk data processing operations", component.Markup);
    }

    [Fact]
    public void Jobs_WithNoJobs_ShowsEmptyState()
    {
        var httpClient = CreateMockHttpClient(
            (HttpStatusCode.OK, JsonContent.Create(Array.Empty<BulkOperation>()))
        );
        Services.AddSingleton(httpClient);

        var component = RenderComponent<Operations>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("No operations found", component.Markup);
        });
    }

    [Fact]
    public void Jobs_WithJobs_DisplaysJobsTable()
    {
        var jobs = new BulkOperation[]
        {
            new BulkOperation
            {
                Id = Guid.NewGuid(),
                OperationName = "test-operation",
                Status = BulkOperationStatus.Completed,
                TotalRows = 100,
                ProcessedRows = 100,
                SuccessfulRows = 95,
                FailedRows = 5,
                CreatedAt = DateTime.UtcNow
            }
        };

        var pagedResult = new { Items = jobs, TotalCount = jobs.Length, Page = 1, PageSize = 20 };
        var httpClient = CreateMockHttpClient(
            (HttpStatusCode.OK, JsonContent.Create(pagedResult))
        );
        Services.AddSingleton(httpClient);

        var component = RenderComponent<Operations>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("test-operation", component.Markup);
            Assert.Contains("Completed", component.Markup);
        });
    }

    [Fact]
    public void JobDetails_WithValidJob_DisplaysJobInformation()
    {
        var job = new BulkOperation
        {
            Id = Guid.NewGuid(),
            OperationName = "test-operation",
            Status = BulkOperationStatus.Completed,
            TotalRows = 100,
            ProcessedRows = 100,
            SuccessfulRows = 95,
            FailedRows = 5,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            CompletedAt = DateTime.UtcNow
        };

        var errorsResult = new
        {
            Items = new[]
            {
                new
                {
                    Id = Guid.NewGuid(),
                    BulkOperationId = job.Id,
                    RowNumber = 10,
                    RowId = (string?)null,
                    ErrorType = "ValidationException",
                    ErrorMessage = "Invalid email format",
                    RowData = (string?)null,
                    CreatedAt = DateTime.UtcNow
                }
            },
            TotalCount = 1,
            Page = 1,
            PageSize = 20,
            HasNextPage = false
        };

        var rowsResult = new
        {
            Items = Array.Empty<object>(),
            TotalCount = 0,
            Page = 1,
            PageSize = 50,
            HasNextPage = false
        };

        var httpClient = CreateMockHttpClient(
            (HttpStatusCode.OK, JsonContent.Create(job)),
            (HttpStatusCode.OK, JsonContent.Create(errorsResult)),
            (HttpStatusCode.OK, JsonContent.Create(rowsResult))
        );
        Services.AddSingleton(httpClient);

        var component = RenderComponent<OperationDetails>(parameters => parameters
            .Add(p => p.id, job.Id));

        component.WaitForAssertion(() =>
        {
            Assert.Contains("test-operation", component.Markup);
            Assert.Contains("100", component.Markup); // Total rows
            Assert.Contains("95", component.Markup);  // Successful rows
            Assert.Contains("5", component.Markup);   // Failed rows
            Assert.Contains("Invalid email format", component.Markup);
        });
    }
}
