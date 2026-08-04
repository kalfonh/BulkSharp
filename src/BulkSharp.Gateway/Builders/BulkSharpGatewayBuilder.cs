using BulkSharp.Gateway.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BulkSharp.Gateway.Builders;

public sealed class BulkSharpGatewayBuilder
{
    internal BulkSharpGatewayBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }
    internal BulkSharpGatewayOptions Options { get; } = new();

    public BulkSharpGatewayBuilder AddBackend(string name, string baseUrl)
    {
        Options.Backends.Add(new GatewayBackendService { Name = name, BaseUrl = baseUrl });
        return this;
    }

    public BulkSharpGatewayBuilder AddBackend(string name)
    {
        return AddBackend(name, $"http+https://{name}");
    }

    public BulkSharpGatewayBuilder ConfigureResilience(Action<BulkSharpGatewayOptions> configure)
    {
        configure(Options);
        return this;
    }

    /// <summary>Additional delegating handlers applied to every backend client.</summary>
    internal List<Type> BackendHandlers { get; } = [];

    /// <summary>
    /// Registers an additional <see cref="DelegatingHandler"/> on every backend HTTP client,
    /// for supplying credentials or propagating context.
    /// </summary>
    /// <remarks>
    /// Token forwarding is only one credential model. Service-to-service client credentials,
    /// mTLS and signed internal headers are equally legitimate, and a host must be able to
    /// supply one without forking the gateway. Handlers run in registration order, ahead of
    /// the resilience pipeline, so retried requests carry the credential.
    /// </remarks>
    /// <typeparam name="THandler">The handler type. Registered as transient.</typeparam>
    public BulkSharpGatewayBuilder AddBackendHandler<THandler>()
        where THandler : DelegatingHandler
    {
        Services.AddTransient<THandler>();
        BackendHandlers.Add(typeof(THandler));
        return this;
    }
}
