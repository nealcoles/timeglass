using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Timeglass.App.Settings;

namespace Timeglass.App.Logging;

/// <summary>
/// Writes the app's log output to rolling text files in
/// <see cref="LogFolder.DefaultPath"/>: one file per day (and a new one past 10 MB), kept for
/// seven days.
/// </summary>
/// <remarks>
/// Code logs through <see cref="Microsoft.Extensions.Logging.ILogger"/> only; Serilog is just the
/// file sink. The level comes from <see cref="Core.Settings.TimeglassSettings.LogLevel"/> and
/// changes live when the settings file changes. Window titles are never logged at Information or above.
/// </remarks>
internal static class FileLogging
{
    /// <summary>Log files older than this are deleted when a new file starts.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    /// <summary>A file that grows past this starts a new one for the same day.</summary>
    public const long FileSizeLimitBytes = 10 * 1024 * 1024;

    /// <summary>File name pattern; Serilog inserts the date, e.g. <c>timeglass-20261103.log</c>.</summary>
    public const string FileNamePattern = "timeglass-.log";

    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Replaces the default providers (console and event log, which a tray app has no use for)
    /// with the log file and the debugger output.
    /// </summary>
    public static ILoggingBuilder AddTimeglassLogging(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddDebug();

        // Let everything through to the providers; the file's level switch decides what is
        // written, so a changed level applies without a restart.
        logging.SetMinimumLevel(LogLevel.Trace);

        // Framework internals are noise at Debug; keep their warnings and errors.
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("System", LogLevel.Warning);

        logging.Services.AddSingleton<ILoggerProvider>(CreateFileProvider);
        return logging;
    }

    /// <summary>Maps a Microsoft.Extensions.Logging level to Serilog's; <c>None</c> writes nothing.</summary>
    public static LogEventLevel ToSerilog(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        // Above Fatal: Serilog's way of switching a logger off.
        _ => LogEventLevel.Fatal + 1,
    };

    private static SerilogLoggerProvider CreateFileProvider(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<TimeglassOptions>>().Value;
        var settings = services.GetRequiredService<LiveSettings>();
        var level = new LoggingLevelSwitch(ToSerilog(settings.Current.Settings.LogLevel));

        // Lives as long as the app (the provider is a singleton), so the handler is never removed.
        settings.Changed += s => level.MinimumLevel = ToSerilog(s.Settings.LogLevel);

        var logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(level)
            .WriteTo.File(
                Path.Combine(LogFolder.Resolve(options), FileNamePattern),
                outputTemplate: OutputTemplate,
                formatProvider: CultureInfo.InvariantCulture,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: null,
                retainedFileTimeLimit: Retention)
            .CreateLogger();

        // Disposing the provider (with the host) flushes and closes the file.
        return new SerilogLoggerProvider(logger, dispose: true);
    }
}
