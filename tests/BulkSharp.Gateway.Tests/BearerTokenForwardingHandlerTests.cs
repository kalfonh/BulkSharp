using System.Net;
using BulkSharp.Gateway.Services;
using Microsoft.AspNetCore.Http;

namespace BulkSharp.Gateway.Tests;

[Trait("Category", "Unit")]
public class BearerTokenForwardingHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Captured { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static async Task<HttpRequestMessage> SendWithInboundAsync(string? inboundAuthorization)
    {
        var context = new DefaultHttpContext();
        if (inboundAuthorization is not null)
            context.Request.Headers.Authorization = inboundAuthorization;

        var capturing = new CapturingHandler();
        var sut = new BearerTokenForwardingHandler(new HttpContextAccessor { HttpContext = context })
        {
            InnerHandler = capturing
        };

        using var client = new HttpClient(sut) { BaseAddress = new Uri("https://backend.test/") };
        await client.GetAsync("api/bulks");

        return capturing.Captured!;
    }

    [Fact]
    public async Task SendAsync_ForwardsInboundBearerToken()
    {
        var captured = await SendWithInboundAsync("Bearer inbound-token");

        captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be("inbound-token");
    }

    [Fact]
    public async Task SendAsync_WithNoInboundToken_SendsNoAuthorizationHeader()
    {
        var captured = await SendWithInboundAsync(null);

        captured.Headers.Authorization.Should().BeNull();
    }

    /// <summary>
    /// Only bearer tokens are forwarded. Forwarding a Basic credential to a backend
    /// would hand it a password the caller never intended it to see.
    /// </summary>
    [Fact]
    public async Task SendAsync_WithNonBearerScheme_DoesNotForward()
    {
        var captured = await SendWithInboundAsync("Basic dXNlcjpwYXNz");

        captured.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_WithBearerButNoValue_DoesNotForward()
    {
        var captured = await SendWithInboundAsync("Bearer");

        captured.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_WithNoHttpContext_DoesNotThrow()
    {
        var capturing = new CapturingHandler();
        var sut = new BearerTokenForwardingHandler(new HttpContextAccessor { HttpContext = null })
        {
            InnerHandler = capturing
        };

        using var client = new HttpClient(sut) { BaseAddress = new Uri("https://backend.test/") };
        var response = await client.GetAsync("api/bulks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturing.Captured!.Headers.Authorization.Should().BeNull();
    }
}
