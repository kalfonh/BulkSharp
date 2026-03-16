namespace BulkSharp.Core.Constants;

/// <summary>Diagnostics constants for OpenTelemetry integration.</summary>
public static class BulkSharpDiagnostics
{
    /// <summary>
    /// The ActivitySource name used by BulkSharp for OpenTelemetry tracing.
    /// Configure your TracerProvider with: <c>builder.AddSource(BulkSharpDiagnostics.ActivitySourceName)</c>
    /// </summary>
    public const string ActivitySourceName = "BulkSharp";
}
