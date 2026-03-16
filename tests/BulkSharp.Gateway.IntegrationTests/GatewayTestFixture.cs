using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BulkSharp.Gateway.IntegrationTests;

/// <summary>
/// Boots a real gateway WebApplication with fake backend HttpMessageHandlers
/// so that all HTTP routing, aggregation, and registry refresh runs end-to-end
/// without hitting any real backend service.
/// </summary>
public sealed class GatewayTestFixture : IDisposable
{
    private WebApplication? _app;

    public HttpClient CreateGatewayClient(params (string Name, FakeBackendHandler Handler)[] backends)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddBulkSharpGateway(gw =>
        {
            foreach (var (name, _) in backends)
                gw.AddBackend(name, $"http://{name}.fake");

            // Fast refresh so registry is populated quickly at startup
            gw.ConfigureResilience(opts => opts.RegistryRefreshInterval = TimeSpan.FromHours(1));
        });

        // Replace the real HttpClients with our fake handlers.
        // ConfigurePrimaryHttpMessageHandler replaces the inner-most handler in
        // the delegating handler chain (including the resilience pipeline added
        // by AddStandardResilienceHandler).
        foreach (var (name, handler) in backends)
        {
            builder.Services.AddHttpClient($"BulkSharpGateway_{name}")
                .ConfigurePrimaryHttpMessageHandler(() => handler);
        }

        _app = builder.Build();
        _app.UseBulkSharpGateway();
        _app.Start();

        // Wait briefly for the background registry refresh to complete.
        // The OperationRegistryRefreshService runs RefreshAsync on startup
        // which calls GetOperationsAsync on each fake backend.
        Thread.Sleep(500);

        var baseUrl = _app.Urls.First();
        return new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public void Dispose()
    {
        _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
