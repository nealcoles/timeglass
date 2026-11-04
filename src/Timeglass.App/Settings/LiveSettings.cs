using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Timeglass.Core.Apps;
using Timeglass.Core.Settings;
using Timeglass.Core.Tracking;

namespace Timeglass.App.Settings;

/// <summary>
/// The current, valid <see cref="TimeglassSettings"/> and what they map to, updated whenever
/// <c>settings.json</c> changes.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IOptionsMonitor{TOptions}"/> throws from <c>CurrentValue</c> once the file holds
/// invalid values, which would stop tracking on every tick. This class creates and validates
/// each reload itself and, when an edit is invalid or a value can't be converted, logs why and
/// keeps the last valid settings.
/// Settings that fail validation at startup still stop the app (<c>ValidateOnStart</c>).
/// </para>
/// <para>
/// Reloads arrive on a file watcher thread. Readers get an immutable <see cref="Snapshot"/>
/// through a reference swap, so they never see half an update.
/// </para>
/// </remarks>
internal sealed partial class LiveSettings : IDisposable
{
    private readonly IOptionsFactory<TimeglassSettings> _factory;
    private readonly Func<ILogger> _logger;
    private readonly AppAliasTable _builtInAliases;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Lock _reloadGate = new();

    private Snapshot _current;

    /// <param name="factory">Creates and validates settings from the current configuration.</param>
    /// <param name="changeTokens">Signal that the configuration changed.</param>
    /// <param name="logger">
    /// Resolved on first use: the log file's level comes from these settings, so the logger
    /// factory cannot exist yet when this is created.
    /// </param>
    /// <param name="builtInAliases">The aliases the user's aliases apply to; defaults to <see cref="AppAliasTable.BuiltIn"/>.</param>
    /// <exception cref="OptionsValidationException">The settings are invalid.</exception>
    public LiveSettings(
        IOptionsFactory<TimeglassSettings> factory,
        IEnumerable<IOptionsChangeTokenSource<TimeglassSettings>> changeTokens,
        Func<ILogger> logger,
        AppAliasTable? builtInAliases = null)
    {
        _factory = factory;
        _logger = logger;
        _builtInAliases = builtInAliases ?? AppAliasTable.BuiltIn;
        _current = CreateSnapshot();

        foreach (var source in changeTokens.Where(s => s.Name == Options.DefaultName))
        {
            _subscriptions.Add(ChangeToken.OnChange(source.GetChangeToken, Reload));
        }
    }

    /// <summary>Raised after valid settings were applied, on the thread that noticed the change.</summary>
    public event Action<Snapshot>? Changed;

    /// <summary>The settings in effect.</summary>
    public Snapshot Current => Volatile.Read(ref _current);

    /// <summary>The tracking options in effect; cheap enough to call on every tick.</summary>
    public TrackingOptions GetTrackingOptions() => Current.Tracking;

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    private Snapshot CreateSnapshot()
    {
        var settings = _factory.Create(Options.DefaultName);
        return new Snapshot(
            settings,
            settings.ToTrackingOptions(TrackingOptions.Default),
            settings.ToAliasTable(_builtInAliases));
    }

    private void Reload()
    {
        Snapshot snapshot;
        lock (_reloadGate)
        {
            try
            {
                snapshot = CreateSnapshot();
            }
            catch (OptionsValidationException ex)
            {
                LogRejected(_logger(), string.Join(" ", ex.Failures));
                return;
            }
            catch (InvalidOperationException ex)
            {
                // The binder could not convert a value, e.g. "idleThreshold": "five minutes".
                LogRejected(_logger(), ex.Message);
                return;
            }

            Volatile.Write(ref _current, snapshot);
        }

        LogApplied(_logger());
        Changed?.Invoke(snapshot);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Settings changed; applied")]
    private static partial void LogApplied(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings changed but are invalid; keeping the previous settings. {Errors}")]
    private static partial void LogRejected(ILogger logger, string errors);

    /// <summary>One consistent set of settings and the values derived from them.</summary>
    /// <param name="Settings">As bound and validated. Do not modify.</param>
    /// <param name="Tracking">The engine's options.</param>
    /// <param name="Aliases">The built-in aliases with the user's applied.</param>
    public sealed record Snapshot(TimeglassSettings Settings, TrackingOptions Tracking, AppAliasTable Aliases);
}
