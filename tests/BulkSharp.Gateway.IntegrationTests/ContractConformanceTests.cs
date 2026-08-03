using System.Text.RegularExpressions;
using BulkSharp.Api;
using BulkSharp.Core.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BulkSharp.Gateway.IntegrationTests;

/// <summary>
/// The gateway must be indistinguishable from a backend to any client: same routes,
/// same verbs, same response shapes. That is the property which lets a single OpenAPI
/// document describe both, instead of maintaining two documents that drift.
/// </summary>
/// <remarks>
/// This test exists because drift is otherwise invisible. It was added after the gateway
/// was found to expose <c>GET /api/bulks/{id}/row-items</c>, a route the API never
/// implemented — a request that could only ever fail, and which a hand-written second
/// OpenAPI document would have faithfully advertised as working.
/// </remarks>
[Trait("Category", "Integration")]
public class ContractConformanceTests
{
    /// <summary>
    /// Routes the gateway deliberately does not proxy, with the reason. Anything else
    /// appearing on one side and not the other is a defect, not a design decision.
    /// </summary>
    private static readonly HashSet<string> KnownGatewayOnlyRoutes = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> KnownApiOnlyRoutes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The convention: every route in the contract is declared once in
    /// <see cref="BulkSharpRoutes"/> and mapped by every implementation. These two tests
    /// enforce the convention itself, so a new route cannot be added to one implementation
    /// and forgotten in the other, and no implementation can invent an unlisted route.
    /// </summary>
    [Theory]
    [MemberData(nameof(ContractRoutes))]
    public void EveryContractRoute_IsMappedByTheApi(string route)
    {
        var mapped = GetApiRoutes().Select(StripVerb).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(
            mapped.Contains(NormalizeRoute(route)),
            $"BulkSharpRoutes declares '{route}' but BulkSharp.Api does not map it.");
    }

    [Theory]
    [MemberData(nameof(ContractRoutes))]
    public void EveryContractRoute_IsMappedByTheGateway(string route)
    {
        var mapped = GetGatewayRoutes().Select(StripVerb).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(
            mapped.Contains(NormalizeRoute(route)),
            $"BulkSharpRoutes declares '{route}' but BulkSharp.Gateway does not map it.");
    }

    [Fact]
    public void NeitherImplementation_MapsAnUndeclaredRoute()
    {
        var declared = BulkSharpRoutes.All.Select(NormalizeRoute).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var undeclared = GetApiRoutes().Concat(GetGatewayRoutes())
            .Select(StripVerb)
            .Where(route => !declared.Contains(route))
            .Distinct()
            .OrderBy(route => route)
            .ToList();

        Assert.True(
            undeclared.Count == 0,
            $"Routes are mapped that BulkSharpRoutes does not declare. Add them to the contract " +
            $"or remove them:{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", undeclared));
    }

    public static TheoryData<string> ContractRoutes()
    {
        var data = new TheoryData<string>();
        foreach (var route in BulkSharpRoutes.All)
            data.Add(route);
        return data;
    }

    private static string StripVerb(string verbAndRoute) => verbAndRoute.Split(' ', 2)[1];

    [Fact]
    public void Gateway_ExposesTheSameRouteSurfaceAsTheApi()
    {
        var apiRoutes = GetApiRoutes();
        var gatewayRoutes = GetGatewayRoutes();

        // Comparing two empty sets would pass and prove nothing. If route discovery
        // silently stops working, fail here rather than reporting conformance.
        Assert.True(
            apiRoutes.Count >= 15,
            $"Only discovered {apiRoutes.Count} API routes; route discovery is broken and this test is vacuous.");
        Assert.True(
            gatewayRoutes.Count >= 15,
            $"Only discovered {gatewayRoutes.Count} gateway routes; route discovery is broken and this test is vacuous.");

        var missingFromGateway = apiRoutes.Except(gatewayRoutes).Except(KnownApiOnlyRoutes).OrderBy(r => r).ToList();
        var extraOnGateway = gatewayRoutes.Except(apiRoutes).Except(KnownGatewayOnlyRoutes).OrderBy(r => r).ToList();

        Assert.True(
            missingFromGateway.Count == 0,
            $"The API exposes routes the gateway does not proxy, so a client generated from the " +
            $"shared contract will 404 against the gateway:{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", missingFromGateway));

        Assert.True(
            extraOnGateway.Count == 0,
            $"The gateway exposes routes no backend implements, so these can only fail when " +
            $"called:{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", extraOnGateway));
    }

    private static HashSet<string> GetApiRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddBulkSharp(b => b
            .UseFileStorage(fs => fs.UseInMemory())
            .UseMetadataStorage(ms => ms.UseInMemory())
            .UseScheduler(s => s.UseImmediate()));
        builder.Services.AddBulkSharpEndpoints();

        var app = builder.Build();
        app.MapBulkSharpEndpoints();

        return DescribeRoutes(app);
    }

    private static HashSet<string> GetGatewayRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddBulkSharpGateway(gw => gw.AddBackend("fake", "http://fake.test"));

        var app = builder.Build();
        app.UseBulkSharpGateway();

        return DescribeRoutes(app);
    }

    private static HashSet<string> DescribeRoutes(WebApplication app)
    {
        // Minimal-API endpoints live on the WebApplication's own data sources. The
        // EndpointDataSource resolved from DI is empty until the app starts, which
        // would make this comparison silently vacuous.
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints);
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            var pattern = NormalizeRoute(endpoint.RoutePattern.RawText ?? string.Empty);
            if (!pattern.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                continue;

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                          ?? ["ANY"];

            foreach (var method in methods)
                routes.Add($"{method.ToUpperInvariant()} {pattern}");
        }

        return routes;
    }

    /// <summary>
    /// Strips route constraints so <c>/api/bulks/{id:guid}</c> and <c>/api/bulks/{id}</c>
    /// compare equal. The constraint is an implementation choice; the contract is the shape.
    /// </summary>
    private static string NormalizeRoute(string pattern) =>
        Regex.Replace(pattern, @"\{(\w+):[^}]+\}", "{$1}");
}
