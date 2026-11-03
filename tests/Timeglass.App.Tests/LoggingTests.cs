using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Timeglass.App.Logging;

namespace Timeglass.App.Tests;

public sealed class LoggingTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string LogDirectory => Path.Combine(_folder.Path, "logs");

    private IHost BuildHost(LogLevel? level = null)
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{TimeglassOptions.Section}:{nameof(TimeglassOptions.DatabasePath)}"] = _folder.DatabasePath,
            [$"{TimeglassOptions.Section}:{nameof(TimeglassOptions.LogDirectory)}"] = LogDirectory,
        };
        if (level is { } l)
        {
            settings[$"{TimeglassOptions.Section}:{nameof(TimeglassOptions.LogLevel)}"] = Enum.GetName(l);
        }

        // An in-memory source keeps values across a reload, unlike values set on other providers.
        var builder = TimeglassHost.CreateBuilder([]);
        builder.Configuration.AddInMemoryCollection(settings);
        return builder.Build();
    }

    /// <summary>Disposes the host (which closes the log file) and returns everything written.</summary>
    private string ReadLogAfterDisposing(IHost host)
    {
        host.Dispose();
        var files = Directory.GetFiles(LogDirectory, "timeglass-*.log");
        files.ShouldNotBeEmpty();
        return string.Concat(files.Order(StringComparer.Ordinal).Select(File.ReadAllText));
    }

    [Fact]
    public void A_forced_exception_on_the_ui_thread_ends_up_in_the_log_and_quits_the_app()
    {
        var host = BuildHost();
        var crashes = host.Services.GetRequiredService<CrashHandler>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        crashes.HandleUiThreadException(new InvalidOperationException("Forced UI failure"));

        crashes.HasCrashed.ShouldBeTrue();
        lifetime.ApplicationStopping.IsCancellationRequested.ShouldBeTrue();
        var log = ReadLogAfterDisposing(host);
        log.ShouldContain("[FTL]");
        log.ShouldContain("System.InvalidOperationException: Forced UI failure");
    }

    [Fact]
    public void An_unhandled_exception_on_another_thread_ends_up_in_the_log()
    {
        var host = BuildHost();
        var crashes = host.Services.GetRequiredService<CrashHandler>();

        crashes.HandleUnhandledException(new IOException("Forced background failure"), isTerminating: true);

        crashes.HasCrashed.ShouldBeTrue();
        var log = ReadLogAfterDisposing(host);
        log.ShouldContain("terminating: True");
        log.ShouldContain("System.IO.IOException: Forced background failure");
    }

    [Fact]
    public void An_unobserved_task_exception_is_logged_without_quitting()
    {
        var host = BuildHost();
        var crashes = host.Services.GetRequiredService<CrashHandler>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        crashes.HandleUnobservedTaskException(new AggregateException(new TimeoutException("Forced task failure")));

        crashes.HasCrashed.ShouldBeFalse();
        lifetime.ApplicationStopping.IsCancellationRequested.ShouldBeFalse();
        ReadLogAfterDisposing(host).ShouldContain("Forced task failure");
    }

    [Fact]
    public void The_log_level_comes_from_the_settings()
    {
        var host = BuildHost(LogLevel.Warning);
        var logger = host.Services.GetRequiredService<ILogger<LoggingTests>>();

        logger.LogInformation("Information entry");
        logger.LogWarning("Warning entry");

        var log = ReadLogAfterDisposing(host);
        log.ShouldNotContain("Information entry");
        log.ShouldContain("Warning entry");
    }

    [Fact]
    public void Debug_is_off_by_default_and_a_changed_level_applies_without_a_restart()
    {
        var host = BuildHost();
        var logger = host.Services.GetRequiredService<ILogger<LoggingTests>>();
        logger.LogDebug("Before the change");

        var configuration = (IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>();
        configuration[$"{TimeglassOptions.Section}:{nameof(TimeglassOptions.LogLevel)}"] = nameof(LogLevel.Debug);
        configuration.Reload();
        logger.LogDebug("After the change");

        var log = ReadLogAfterDisposing(host);
        log.ShouldNotContain("Before the change");
        log.ShouldContain("After the change");
    }

    [Theory]
    [InlineData(LogLevel.Trace, LogEventLevel.Verbose)]
    [InlineData(LogLevel.Debug, LogEventLevel.Debug)]
    [InlineData(LogLevel.Information, LogEventLevel.Information)]
    [InlineData(LogLevel.Warning, LogEventLevel.Warning)]
    [InlineData(LogLevel.Error, LogEventLevel.Error)]
    [InlineData(LogLevel.Critical, LogEventLevel.Fatal)]
    public void Levels_map_to_serilog(LogLevel level, LogEventLevel expected) =>
        FileLogging.ToSerilog(level).ShouldBe(expected);

    [Fact]
    public void None_writes_nothing() =>
        FileLogging.ToSerilog(LogLevel.None).ShouldBeGreaterThan(LogEventLevel.Fatal);

    [Fact]
    public void The_log_folder_defaults_to_local_app_data()
    {
        LogFolder.Resolve(new TimeglassOptions()).ShouldBe(LogFolder.DefaultPath);
        LogFolder.DefaultPath.ShouldEndWith(Path.Combine("Timeglass", "logs"));
        LogFolder.Resolve(new TimeglassOptions { LogDirectory = @"D:\logs" }).ShouldBe(@"D:\logs");
    }
}
