using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Timeglass.Api;
using Timeglass.Core.Settings;
using Timeglass.Storage.Settings;

namespace Timeglass.App.Settings;

/// <summary>
/// The dashboard's view of <c>settings.json</c> (#36): reads what <see cref="LiveSettings"/> has in
/// effect and writes the file, which the app then applies like any other edit.
/// </summary>
/// <remarks>
/// After writing the file, <see cref="SaveAsync"/> reloads the configuration itself rather than
/// waiting for the file watcher, so the new settings are in effect when it returns and a request
/// that reads them right after sees them. The watcher reloads the same file once more later.
/// </remarks>
internal sealed partial class FileSettingsStore : ISettingsStore
{
    private readonly LiveSettings _settings;
    private readonly SettingsConfiguration _source;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();

    public FileSettingsStore(LiveSettings settings, SettingsConfiguration source, ILogger<FileSettingsStore> logger)
    {
        _settings = settings;
        _source = source;
        _logger = logger;
    }

    public TimeglassSettings Current => _settings.Current.Settings;

    public Task SaveAsync(TimeglassSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var path = _source.Path ?? throw new InvalidOperationException("The settings were not loaded from a file, so they can't be saved.");
        return Task.Run(
            () =>
            {
                lock (_gate)
                {
                    SettingsFile.Save(path, settings);
                    (_source.Configuration as IConfigurationRoot)?.Reload();
                }

                LogSaved(_logger, path);
            },
            cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Settings saved from the dashboard to {Path}")]
    private static partial void LogSaved(ILogger logger, string path);
}
