namespace BulkSharp.Gateway.Configuration;

public sealed class BulkSharpGatewayOptions
{
    public List<GatewayBackendService> Backends { get; } = new();
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int HttpRetryCount { get; set; } = 2;
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan RegistryRefreshInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan FanOutTimeoutPerBackend { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// When true, the caller's bearer token is forwarded to backend services so they can
    /// authorize the original caller. Defaults to true.
    /// </summary>
    /// <remarks>
    /// Set false when supplying a different credential model through
    /// <c>AddBackendHandler</c>, such as service-to-service client credentials.
    /// </remarks>
    public bool ForwardBearerToken { get; set; } = true;
}
