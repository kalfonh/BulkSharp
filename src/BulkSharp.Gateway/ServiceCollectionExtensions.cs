using BulkSharp.Gateway;
using BulkSharp.Gateway.Builders;
using BulkSharp.Gateway.Configuration;
using BulkSharp.Gateway.HealthChecks;
using BulkSharp.Gateway.Registry;
using BulkSharp.Gateway.Routing;
using BulkSharp.Gateway.Services;
using Microsoft.Extensions.Http.Resilience;

namespace Microsoft.Extensions.DependencyInjection;

public static class BulkSharpGatewayServiceCollectionExtensions
{
    public static IServiceCollection AddBulkSharpGateway(
        this IServiceCollection services,
        Action<BulkSharpGatewayBuilder> configure)
    {
        if (services.Any(s => s.ServiceType == typeof(BulkSharpGatewayMarker)))
            return services;

        services.AddSingleton<BulkSharpGatewayMarker>();

        var builder = new BulkSharpGatewayBuilder(services);
        configure(builder);

        var options = builder.Options;
        services.AddSingleton(options);

        services.AddMemoryCache();
        services.AddSingleton<IOperationRegistry, OperationRegistry>();
        services.AddSingleton<IBackendClientFactory, BackendClientFactory>();
        services.AddSingleton<GatewayRouter>();
        services.AddSingleton<GatewayAggregator>();
        services.AddHostedService<OperationRegistryRefreshService>();

        // Tagged "ready" so hosts can separate readiness from liveness: an unreachable
        // backend should not cause the gateway itself to be restarted.
        services.AddHealthChecks()
            .AddCheck<BackendHealthCheck>("bulksharp-backends", tags: ["ready"]);

        services.AddHttpContextAccessor();
        services.AddTransient<BearerTokenForwardingHandler>();

        // Register a named HttpClient per backend with resilience
        foreach (var backend in options.Backends)
        {
            var clientBuilder = services.AddHttpClient($"BulkSharpGateway_{backend.Name}", http =>
            {
                http.BaseAddress = new Uri(backend.BaseUrl.TrimEnd('/') + "/");
                http.Timeout = options.HttpTimeout;
            });

            // Credential handlers must precede the resilience handler so that retried
            // requests still carry the credential.
            if (options.ForwardBearerToken)
                clientBuilder.AddHttpMessageHandler<BearerTokenForwardingHandler>();

            foreach (var handlerType in builder.BackendHandlers)
                clientBuilder.AddHttpMessageHandler(sp =>
                    (DelegatingHandler)sp.GetRequiredService(handlerType));

            clientBuilder.AddStandardResilienceHandler();
        }

        return services;
    }
}

internal sealed class BulkSharpGatewayMarker;
