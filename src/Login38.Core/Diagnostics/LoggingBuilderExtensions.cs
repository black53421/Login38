using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Login38.Core.Diagnostics;

/// <summary>Registration helpers for the launcher's log sink.</summary>
public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Adds the launcher log sink: two log files plus a live feed for the in-app log
    /// view. File writing follows <see cref="LauncherLogOptions.WriteLogsEnvironmentVariable"/>
    /// unless <paramref name="configure"/> overrides it.
    /// </summary>
    public static ILoggingBuilder AddLauncherLogging(
        this ILoggingBuilder builder,
        Action<LauncherLogOptions>? configure = null)
    {
        builder.Services.TryAddSingleton<ILogBroadcast, LogBroadcast>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, LauncherLoggerProvider>());

        builder.Services.AddOptions<LauncherLogOptions>()
            .Configure(options =>
            {
                options.WriteToFile =
                    EnvironmentSwitch.IsEnabled(LauncherLogOptions.WriteLogsEnvironmentVariable);
            });

        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        return builder;
    }
}
