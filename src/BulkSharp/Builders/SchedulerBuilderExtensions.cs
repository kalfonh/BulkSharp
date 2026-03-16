using BulkSharp.Builders;
using BulkSharp.Core.Abstractions.Processing;
using BulkSharp.Processing.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BulkSharp;

/// <summary>
/// Built-in scheduler extension methods for SchedulerBuilder.
/// </summary>
public static class SchedulerBuilderExtensions
{
    /// <summary>
    /// Use high-performance Channels-based scheduler with configurable workers.
    /// </summary>
    public static SchedulerBuilder UseChannels(this SchedulerBuilder builder, Action<ChannelsSchedulerOptions>? configure = null)
    {
        builder.EnsureNotConfigured();

        if (configure != null)
            builder.Services.Configure(configure);
        else
            builder.Services.AddOptions<ChannelsSchedulerOptions>();

        builder.Services.PostConfigure<ChannelsSchedulerOptions>(o => o.Validate());

        builder.Services.AddSingleton<ChannelsScheduler>();
        builder.Services.AddSingleton<IBulkScheduler>(provider => provider.GetRequiredService<ChannelsScheduler>());
        builder.Services.AddHostedService(provider => provider.GetRequiredService<ChannelsScheduler>());

        return builder;
    }

    /// <summary>
    /// Use immediate inline execution (for testing only - operations process synchronously).
    /// </summary>
    public static SchedulerBuilder UseImmediate(this SchedulerBuilder builder)
    {
        builder.EnsureNotConfigured();
        builder.Services.AddSingleton<IBulkScheduler, ImmediateScheduler>();
        return builder;
    }
}
