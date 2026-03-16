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
}
