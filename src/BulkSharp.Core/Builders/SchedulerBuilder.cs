using BulkSharp.Core.Abstractions.Processing;
using Microsoft.Extensions.DependencyInjection;

namespace BulkSharp.Builders;

/// <summary>
/// Builder for configuring scheduler options
/// </summary>
public sealed class SchedulerBuilder
{
    private readonly IServiceCollection _services;
    private bool _configured;

    /// <summary>
    /// The service collection for registering additional dependencies.
    /// Used by scheduler extension packages.
    /// </summary>
    public IServiceCollection Services => _services;

    internal SchedulerBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Marks this builder as configured. Called by extension methods to enforce single-configuration guard.
    /// </summary>
    public void EnsureNotConfigured()
    {
        if (_configured)
            throw new InvalidOperationException("A scheduler has already been configured.");
        _configured = true;
    }

    /// <summary>
    /// Use a custom scheduler. If the implementation also implements IHostedService,
    /// it is automatically registered as a hosted service.
    /// </summary>
    public SchedulerBuilder UseCustom<T>() where T : class, IBulkScheduler
    {
        EnsureNotConfigured();
        _services.AddSingleton<T>();
        _services.AddSingleton<IBulkScheduler>(sp => sp.GetRequiredService<T>());
        if (typeof(Microsoft.Extensions.Hosting.IHostedService).IsAssignableFrom(typeof(T)))
        {
            _services.AddHostedService(sp => (Microsoft.Extensions.Hosting.IHostedService)sp.GetRequiredService<T>());
        }
        return this;
    }

    /// <summary>
    /// Use a custom scheduler registration with full control over service registration.
    /// </summary>
    public SchedulerBuilder UseCustom(Action<IServiceCollection> configure)
    {
        EnsureNotConfigured();
        configure(_services);
        return this;
    }
}
