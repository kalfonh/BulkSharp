using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace BulkSharp.Gateway.Services;

/// <summary>
/// Forwards the inbound request's bearer token to the backend service.
/// </summary>
/// <remarks>
/// Without this the gateway authenticates the caller at the edge and then calls backends
/// anonymously — a confused deputy. Backends cannot enforce anything, and the identity
/// the API attributes operations to is lost at the first hop.
/// <para>
/// This is the default credential model. Hosts needing service-to-service credentials,
/// mTLS or a signed internal header should set <c>ForwardBearerToken</c> to false and
/// supply their own handler via <c>AddBackendHandler</c>.
/// </para>
/// </remarks>
/// <param name="httpContextAccessor">Accessor for the inbound request.</param>
public sealed class BearerTokenForwardingHandler(IHttpContextAccessor httpContextAccessor)
    : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var inbound = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();

        if (!string.IsNullOrEmpty(inbound) &&
            AuthenticationHeaderValue.TryParse(inbound, out var header) &&
            string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(header.Parameter))
        {
            request.Headers.Authorization = header;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
