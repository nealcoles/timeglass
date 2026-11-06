using Microsoft.Extensions.Options;
using Timeglass.Api;
using Timeglass.Core.Domain;
using Timeglass.Core.Ports;
using Timeglass.Core.Settings;
using Timeglass.Storage;

namespace Timeglass.DevHost;

/// <summary>Command-line options of the dev host, e.g. <c>--port 5400 --workdays 10</c>.</summary>
public sealed class DevHostOptions
{
    /// <summary>
    /// Port on 127.0.0.1. Defaults to <c>TIMEGLASS_API_PORT</c> if set (the variable the Vite
    /// proxy reads), else <see cref="TimeglassSettings.DefaultApiPort"/>.
    /// </summary>
    public int? Port { get; set; }

    /// <summary>Seed of the demo data. Another seed gives other, equally realistic, days.</summary>
    public int Seed { get; set; } = DemoData.DefaultSeed;

    /// <summary>How many workdays before today get data.</summary>
    public int Workdays { get; set; } = DemoData.DefaultWorkdays;

    /// <summary>
    /// Database file to use and keep. By default a new file in a temp folder, deleted on exit.
    /// An existing file is used as it is, without demo data added.
    /// </summary>
    public string? Database { get; set; }

    /// <summary>Report tracking as paused, to work on the paused state of the dashboard.</summary>
    public bool Paused { get; set; }
}

/// <summary>Builds the dev host: the API over a demo database on 127.0.0.1.</summary>
public static class DevHost
{
    /// <summary>The configuration key the Vite dev proxy also reads for the API port.</summary>
    public const string PortVariable = "TIMEGLASS_API_PORT";

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        var options = builder.Configuration.Get<DevHostOptions>() ?? new DevHostOptions();
        options.Port ??= builder.Configuration.GetValue<int?>(PortVariable) ?? TimeglassSettings.DefaultApiPort;

        // Loopback only, like the real app: the demo data is harmless, but the habit isn't.
        builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");

        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.AddSingleton<DevDatabase>();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<DevDatabase>().Database);
        builder.Services.AddSingleton<IntervalRepository>();
        builder.Services.AddSingleton<IReportingQueries>(sp => sp.GetRequiredService<IntervalRepository>());
        builder.Services.AddSingleton<ITrackingState>(new DevTrackingState(options.Paused));
        builder.Services.AddSingleton<ISettingsStore, InMemorySettingsStore>();
        builder.Services.AddSingleton<IAutostart, InMemoryAutostart>();
        builder.Services.AddSingleton<IDataDeletion>(sp => new RepositoryDeletion(sp.GetRequiredService<IntervalRepository>()));
        builder.Services.AddHostedService<DemoSeeder>();
        builder.Services.AddTimeglassApi();

        var app = builder.Build();
        app.MapTimeglassApi();
        return app;
    }
}

/// <summary>
/// Pausing without a tracker: the dev host only remembers the state, so the dashboard's paused
/// state can be worked on. A pause with an end stays paused after it, until resumed.
/// </summary>
internal sealed class DevTrackingState(bool paused) : ITrackingState
{
    private readonly Lock _gate = new();
    private bool _paused = paused;
    private DateTimeOffset? _until;

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _paused;
            }
        }
    }

    public DateTimeOffset? PausedUntil
    {
        get
        {
            lock (_gate)
            {
                return _until;
            }
        }
    }

    public Task PauseAsync(DateTimeOffset? until)
    {
        lock (_gate)
        {
            (_paused, _until) = (true, until);
        }

        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        lock (_gate)
        {
            (_paused, _until) = (false, null);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Settings kept in memory for the dev host's lifetime, starting from the defaults.</summary>
internal sealed class InMemorySettingsStore : ISettingsStore
{
    private TimeglassSettings _current = new();

    public TimeglassSettings Current => Volatile.Read(ref _current);

    public Task SaveAsync(TimeglassSettings settings, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _current, settings);
        return Task.CompletedTask;
    }
}

/// <summary>A start-with-Windows switch that only remembers its state.</summary>
internal sealed class InMemoryAutostart : IAutostart
{
    private volatile bool _enabled;

    public bool IsEnabled => _enabled;

    public void SetEnabled(bool enabled) => _enabled = enabled;
}

/// <summary>Deletes straight from the database: the dev host has no tracker with an open interval.</summary>
internal sealed class RepositoryDeletion(IntervalRepository repository) : IDataDeletion
{
    public Task DeleteRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        repository.DeleteRangeAsync(from, to, cancellationToken).AsTask();
}

/// <summary>The dev host's database file: a temp file deleted on dispose, unless one was given.</summary>
internal sealed class DevDatabase : IDisposable
{
    private readonly string? _tempFolder;

    public DevDatabase(IOptions<DevHostOptions> options)
    {
        var path = options.Value.Database;
        IsNew = path is null || !File.Exists(path);
        if (path is null)
        {
            _tempFolder = Path.Combine(Path.GetTempPath(), "timeglass-devhost", Guid.NewGuid().ToString("N"));
            path = Path.Combine(_tempFolder, "timeglass.db");
        }

        Database = TimeglassDatabase.Open(path);
    }

    public TimeglassDatabase Database { get; }

    /// <summary>True if the file was created now and needs demo data.</summary>
    public bool IsNew { get; }

    public bool IsTemporary => _tempFolder is not null;

    public void Dispose()
    {
        Database.Dispose();
        if (_tempFolder is null)
        {
            return;
        }

        try
        {
            Directory.Delete(_tempFolder, recursive: true);
        }
        catch (IOException)
        {
            // Still open somewhere; it's in the temp folder, so the OS cleans it up eventually.
        }
    }
}

/// <summary>Fills a new database with <see cref="DemoData"/> before the server starts taking requests.</summary>
internal sealed partial class DemoSeeder(
    DevDatabase database,
    IntervalRepository repository,
    TimeProvider time,
    IOptions<DevHostOptions> options,
    ILogger<DemoSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (database.IsNew)
        {
            var intervals = DemoData.Generate(time.GetUtcNow(), time.LocalTimeZone, o.Workdays, o.Seed);
            await repository.ApplyAsync(
                [.. intervals.Select(i => new IntervalChange(i.IsOpen ? IntervalChangeKind.Opened : IntervalChangeKind.Closed, i))],
                cancellationToken).ConfigureAwait(false);
            await repository.FlushAsync(cancellationToken).ConfigureAwait(false);
            LogSeeded(logger, intervals.Count, o.Workdays, o.Seed, database.Database.Path, database.IsTemporary ? "deleted on exit" : "kept");
        }
        else
        {
            LogExisting(logger, database.Database.Path);
        }

        LogListening(logger, o.Port ?? 0);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} demo intervals over today and {Workdays} workdays (seed {Seed}) into {Path}, {Fate}")]
    private static partial void LogSeeded(ILogger logger, int count, int workdays, int seed, string path, string fate);

    [LoggerMessage(Level = LogLevel.Information, Message = "Using the existing database {Path} as it is")]
    private static partial void LogExisting(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "API on http://127.0.0.1:{Port}/api, OpenAPI document at /openapi/v1.json")]
    private static partial void LogListening(ILogger logger, int port);
}
