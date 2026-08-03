using System.Security.Claims;

namespace BulkSharp.Api;

/// <summary>
/// Resolves the identity a bulk operation is attributed to.
/// </summary>
/// <remarks>
/// Register your own implementation before or after <c>AddBulkSharpEndpoints()</c> to
/// change how the identity is derived; the built-in registration does not overwrite it.
/// </remarks>
public interface IBulkUserResolver
{
    /// <summary>
    /// Returns the identity to record as the operation's creator, or null when the
    /// request carries no usable identity.
    /// </summary>
    /// <param name="principal">The principal on the current request.</param>
    string? ResolveUser(ClaimsPrincipal principal);
}

/// <summary>
/// Default resolver using standard claims: the subject identifier, then
/// <c>preferred_username</c>, then the identity name.
/// </summary>
/// <remarks>
/// Deliberately claim-agnostic beyond the OIDC and .NET conventions. Hardcoding a
/// single identity provider's claim layout would bake one deployment's assumptions
/// into the library.
/// </remarks>
public sealed class ClaimsBulkUserResolver : IBulkUserResolver
{
    /// <inheritdoc />
    public string? ResolveUser(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity?.IsAuthenticated != true)
            return null;

        var resolved = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? principal.FindFirstValue("preferred_username")
            ?? principal.Identity.Name;

        return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
    }
}
