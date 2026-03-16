namespace BulkSharp.Processing.Constants;

/// <summary>
/// Shared JSON serialization options for BulkSharp internal serialization and deserialization.
/// <para>
/// <b>Thread safety:</b> .NET 8 automatically locks <see cref="JsonSerializerOptions"/>
/// on first serialization use, making the instance effectively immutable after startup.
/// <c>MakeReadOnly()</c> is intentionally not called because it triggers a
/// <see cref="TypeInitializationException"/> when invoked on a static field initializer
/// before the options have been fully constructed.
/// </para>
/// <para>
/// <b>Do not modify these instances after application startup.</b>
/// </para>
/// </summary>
internal static class BulkSharpJsonDefaults
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32
    };
}
