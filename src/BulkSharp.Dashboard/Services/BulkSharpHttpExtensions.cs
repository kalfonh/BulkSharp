using System.Net.Http.Json;
using BulkSharp.Core.Contracts;

namespace BulkSharp.Dashboard.Services;

/// <summary>
/// HTTP helpers that apply the BulkSharp response contract.
/// </summary>
/// <remarks>
/// The <c>System.Net.Http.Json</c> extension methods take their options per call and
/// offer no per-client default, so the contract would otherwise have to be repeated at
/// every call site. These wrappers keep it in one place.
/// <para>
/// This is a seam, not the destination: once the shared response DTOs exist, the pages
/// should consume a typed API client that owns serialization entirely and exposes no
/// <see cref="HttpClient"/> at all.
/// </para>
/// </remarks>
internal static class BulkSharpHttpExtensions
{
    /// <summary>Sends a GET and deserializes the response using the BulkSharp contract.</summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="client">The HTTP client.</param>
    /// <param name="requestUri">The request URI.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static Task<T?> GetFromBulkSharpJsonAsync<T>(
        this HttpClient client,
        string requestUri,
        CancellationToken cancellationToken = default)
        => client.GetFromJsonAsync<T>(requestUri, BulkSharpJsonSerialization.Options, cancellationToken);

    /// <summary>Deserializes a response body using the BulkSharp contract.</summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="content">The response content.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static Task<T?> ReadFromBulkSharpJsonAsync<T>(
        this HttpContent content,
        CancellationToken cancellationToken = default)
        => content.ReadFromJsonAsync<T>(BulkSharpJsonSerialization.Options, cancellationToken);
}
