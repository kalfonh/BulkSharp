using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BulkSharp.Api;
using BulkSharp.Core.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkSharp.Api.Tests;

/// <summary>
/// Authenticates every request as a user holding whichever scopes the fixture is
/// configured with, so read and operate policies can be exercised independently.
/// </summary>
internal sealed class ScopedTestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Scopes granted to the caller. Empty means authenticated with no scopes.</summary>
    internal static string[] Scopes { get; set; } = [];

    /// <summary>When false, requests arrive unauthenticated.</summary>
    internal static bool Authenticate { get; set; } = true;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Authenticate)
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = Scopes.Select(scope => new Claim("scope", scope)).ToList();
        var identity = new ClaimsIdentity(claims, "test");
        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name)));
    }
}

[Trait("Category", "Integration")]
public class AuthorizationTests : IAsyncLifetime
{
    private const string ReadPolicy = "bulk:read";
    private const string OperatePolicy = "bulk:operate";

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

        builder.Services.AddAuthentication("test")
            .AddScheme<AuthenticationSchemeOptions, ScopedTestAuthHandler>("test", null);

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(ReadPolicy, p => p.RequireClaim("scope", "read", "operate"))
            .AddPolicy(OperatePolicy, p => p.RequireClaim("scope", "operate"));

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapBulkSharpEndpoints(new BulkSharpAuthorizationOptions
        {
            ReadPolicy = ReadPolicy,
            OperatePolicy = OperatePolicy
        });

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        ScopedTestAuthHandler.Authenticate = true;
        ScopedTestAuthHandler.Scopes = [];
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("/api/operations")]
    [InlineData("/api/bulks")]
    public async Task ReadEndpoints_RequireAuthentication(string path)
    {
        ScopedTestAuthHandler.Authenticate = false;

        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/operations")]
    [InlineData("/api/bulks")]
    public async Task ReadEndpoints_RejectAuthenticatedCallerWithoutReadScope(string path)
    {
        ScopedTestAuthHandler.Authenticate = true;
        ScopedTestAuthHandler.Scopes = [];

        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/operations")]
    [InlineData("/api/bulks")]
    public async Task ReadEndpoints_AllowReadScope(string path)
    {
        ScopedTestAuthHandler.Authenticate = true;
        ScopedTestAuthHandler.Scopes = ["read"];

        var response = await _client.GetAsync(path);

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// The point of separating the two policies: a viewer must not be able to mutate.
    /// With a single policy this request would succeed.
    /// </summary>
    [Fact]
    public async Task MutatingEndpoint_RejectsReadOnlyCaller()
    {
        ScopedTestAuthHandler.Authenticate = true;
        ScopedTestAuthHandler.Scopes = ["read"];

        var response = await _client.PostAsync($"/api/bulks/{Guid.NewGuid()}/cancel", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MutatingEndpoint_AllowsOperateScope()
    {
        ScopedTestAuthHandler.Authenticate = true;
        ScopedTestAuthHandler.Scopes = ["operate"];

        var response = await _client.PostAsync($"/api/bulks/{Guid.NewGuid()}/cancel", null);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
