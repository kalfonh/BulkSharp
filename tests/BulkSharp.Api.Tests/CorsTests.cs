using BulkSharp.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BulkSharp.Api.Tests;

[Trait("Category", "Integration")]
public class CorsTests : IAsyncLifetime
{
    private const string AllowedOrigin = "https://admin.example.com";

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
        builder.Services.AddBulkSharpEndpoints();
        builder.Services.AddBulkSharpCors(AllowedOrigin);

        _app = builder.Build();
        _app.UseCors(BulkSharpCorsExtensions.PolicyName);
        _app.MapBulkSharpEndpoints();

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
    public async Task Preflight_FromAllowedOrigin_IsPermitted()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/bulks");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        Assert.Contains(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_FromUnknownOrigin_IsNotAllowed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/bulks");
        request.Headers.Add("Origin", "https://evil.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task ActualRequest_ExposesContentDispositionForDownloads()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/bulks");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await _client.SendAsync(request);

        // Without this, a browser client cannot read the filename from the file,
        // export or template download endpoints.
        Assert.Contains(
            "Content-Disposition",
            response.Headers.GetValues("Access-Control-Expose-Headers"));
    }

    [Fact]
    public void AddBulkSharpCors_WithNoOrigins_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddBulkSharpCors());
    }

    [Fact]
    public void AddBulkSharpCors_WithWildcard_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddBulkSharpCors("*"));
    }
}
