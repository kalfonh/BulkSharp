namespace BulkSharp.Core.Contracts;

/// <summary>
/// Authorization policies applied to the BulkSharp endpoints.
/// </summary>
/// <remarks>
/// Part of the contract rather than of either implementation, so <c>BulkSharp.Api</c> and
/// <c>BulkSharp.Gateway</c> are configured the same way without the gateway taking a
/// dependency on the processing engine it does not use.
/// <para>
/// BulkSharp only knows the policy names. The host defines what they require, so the
/// library carries no assumptions about roles, scopes or claim layouts.
/// </para>
/// <para>
/// Reads and writes are separated because most deployments have viewers who must not be
/// able to submit, cancel or retry. Applying one policy to both would force every such
/// host to bolt an endpoint filter on top.
/// </para>
/// </remarks>
public sealed class BulkSharpAuthorizationOptions
{
    /// <summary>
    /// Policy required to read operations, rows, errors, exports and downloads.
    /// Null leaves read endpoints unauthorized.
    /// </summary>
    public string? ReadPolicy { get; init; }

    /// <summary>
    /// Policy required to create, validate, cancel, retry or signal operations.
    /// Null falls back to <see cref="ReadPolicy"/>.
    /// </summary>
    public string? OperatePolicy { get; init; }

    /// <summary>
    /// The policy governing mutating endpoints, after applying the fallback to
    /// <see cref="ReadPolicy"/>.
    /// </summary>
    public string? EffectiveOperatePolicy => OperatePolicy ?? ReadPolicy;
}
