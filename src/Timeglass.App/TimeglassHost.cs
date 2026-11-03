using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Timeglass.App.Logging;
using Timeglass.Core.Apps;
using Timeglass.Core.Ports;
using Timeglass.Core.Tracking;
using Timeglass.Storage;
using Timeglass.Windows.Foreground;
using Timeglass.Windows.Idle;
using Timeglass.Windows.Sessions;

namespace Timeglass.App;

/// <summary>The composition root: the only place that knows every project and wires them up.</summary>
internal static partial class TimeglassHost
{
    /// <summary>
    /// Creates the generic host builder with configuration, logging and every service registered.
    /// The container validates the whole graph when it is built, so a missing registration fails
    /// at startup (and in the composition test) instead of on first use.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ApplicationName = "Timeglass",
            // Started from autostart or a shortcut, the working directory can be anything.
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        // There is no console; the tray menu and session events end the app.
        builder.Services.Configure<ConsoleLifetimeOptions>(o => o.SuppressStatusMessages = true);
        builder.Logging.AddTimeglassLogging();

        AddTimeglass(builder.Services);
        return builder;
    }

    /// <summary>Registers storage, the engine, the Windows sources and the host services.</summary>
    public static IServiceCollection AddTimeglass(IServiceCollection services)
    {
        services.AddOptions<TimeglassOptions>().BindConfiguration(TimeglassOptions.Section);
        services.AddSingleton(TimeProvider.System);

        // Storage. Opening the database applies migrations and closes intervals left open by a crash.
        services.AddSingleton(OpenDatabase);
        services.AddSingleton<IntervalRepository>();
        services.AddSingleton<IIntervalSink>(sp => sp.GetRequiredService<IntervalRepository>());

        // Engine. Settings (#22) will replace these defaults.
        services.AddSingleton(AppAliasTable.BuiltIn);
        services.AddSingleton<IAppIdentityResolver, AliasAppIdentityResolver>();
        services.AddSingleton(TrackingOptions.Default);
        services.AddSingleton(sp => new TrackingEngine(
            sp.GetRequiredService<IAppIdentityResolver>(),
            sp.GetRequiredService<TrackingOptions>()));
        services.AddSingleton<TrackingService>();

        // Windows sources.
        services.AddSingleton(new ForegroundWatcherOptions());
        services.AddSingleton<Win32ForegroundWindowSource>();
        services.AddSingleton<IForegroundWindowSource>(sp => sp.GetRequiredService<Win32ForegroundWindowSource>());
        services.AddSingleton<IIdleSource, Win32IdleSource>();
        services.AddSingleton<Win32SessionEventSource>();
        services.AddSingleton<ISessionEventSource>(sp => sp.GetRequiredService<Win32SessionEventSource>());

        // Host services. The embedded API (#27) registers here as another hosted service.
        services.AddSingleton<TrackingController>();
        services.AddHostedService(sp => sp.GetRequiredService<TrackingController>());
        services.AddSingleton<TodayActiveTime>();
        services.AddSingleton<DashboardLauncher>();
        services.AddSingleton<LogFolder>();
        services.AddSingleton<CrashHandler>();

        return services;
    }

    /// <summary>
    /// Stops and disposes the host off the UI thread. Called after the message loop has ended,
    /// when nothing pumps the WinForms synchronization context any more.
    /// </summary>
    public static void StopAndDispose(IHost host) =>
        Task.Run(async () =>
        {
            try
            {
                await host.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                if (host is IAsyncDisposable asyncDisposable)
                {
                    // IntervalRepository is only IAsyncDisposable: it drains its queue to the database.
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    host.Dispose();
                }
            }
        }).GetAwaiter().GetResult();

    private static TimeglassDatabase OpenDatabase(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<TimeglassOptions>>().Value;
        var logger = services.GetRequiredService<ILogger<TimeglassDatabase>>();

        var database = TimeglassDatabase.Open(options.DatabasePath ?? TimeglassDatabase.DefaultPath);
        LogDatabaseOpened(logger, database.Path, database.RecoveredIntervals);
        return database;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Opened {Path}; closed {Recovered} interval(s) left open by a previous run")]
    private static partial void LogDatabaseOpened(ILogger logger, string path, int recovered);
}
