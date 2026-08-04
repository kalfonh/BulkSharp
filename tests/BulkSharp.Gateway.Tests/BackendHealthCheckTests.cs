using System.Net;
using BulkSharp.Gateway.HealthChecks;
using BulkSharp.Gateway.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BulkSharp.Gateway.Tests;

[Trait("Category", "Unit")]
public class BackendHealthCheckTests
{
    private readonly Mock<IBackendClientFactory> _clientFactory = new();

    private static Mock<IBackendClient> Backend(string name, HttpStatusCode? status, Exception? throws = null)
    {
        var client = new Mock<IBackendClient>();
        client.SetupGet(c => c.ServiceName).Returns(name);

        var setup = client.Setup(c => c.GetOperationsAsync(It.IsAny<CancellationToken>()));
        if (throws is not null)
            setup.ThrowsAsync(throws);
        else
            setup.ReturnsAsync(new HttpResponseMessage(status!.Value));

        return client;
    }

    private Task<HealthCheckResult> CheckAsync(params Mock<IBackendClient>[] backends)
    {
        _clientFactory.Setup(f => f.GetAllClients()).Returns(backends.Select(b => b.Object));
        return new BackendHealthCheck(_clientFactory.Object)
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
    }

    [Fact]
    public async Task AllBackendsReachable_ReportsHealthy()
    {
        var result = await CheckAsync(Backend("service-a", HttpStatusCode.OK));

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task OneBackendUnreachable_ReportsDegraded()
    {
        var result = await CheckAsync(
            Backend("service-a", HttpStatusCode.OK),
            Backend("service-b", null, new HttpRequestException("refused")));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data.Should().ContainKey("service-b");
        result.Data.Should().NotContainKey("service-a");
    }

    [Fact]
    public async Task AllBackendsUnreachable_ReportsUnhealthy()
    {
        var result = await CheckAsync(
            Backend("service-a", null, new HttpRequestException("refused")),
            Backend("service-b", HttpStatusCode.InternalServerError));

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    /// <summary>
    /// A gateway with no backends starts cleanly and answers every query with an empty
    /// list. Reporting healthy would make the worst failure mode invisible.
    /// </summary>
    [Fact]
    public async Task NoBackendsConfigured_ReportsUnhealthy()
    {
        var result = await CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("No backends are configured");
    }

    [Fact]
    public async Task BackendReturningErrorStatus_IsReportedWithThatStatus()
    {
        var result = await CheckAsync(
            Backend("service-a", HttpStatusCode.OK),
            Backend("service-b", HttpStatusCode.ServiceUnavailable));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data["service-b"].Should().Be(503);
    }
}
