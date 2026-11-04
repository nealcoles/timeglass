using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Timeglass.Core.Apps;
using Timeglass.Core.Tracking;

namespace Timeglass.Core.Settings;

/// <summary>
/// The user's settings, stored in <c>settings.json</c> (see <c>SettingsFile</c> in Storage) and
/// applied without a restart when the file changes.
/// </summary>
/// <remarks>
/// A mutable class so the configuration binder can fill it; treat a bound instance as read-only.
/// Validation uses data annotations plus <see cref="Validate"/> for the rules that need more than
/// one property. Time spans are written as <c>hh:mm:ss</c>.
/// </remarks>
public sealed class TimeglassSettings : IValidatableObject
{
    /// <summary>The port the dashboard's dev proxy expects (ADR 0003).</summary>
    public const int DefaultApiPort = 5381;

    /// <summary>
    /// After this long without keyboard or mouse input the user counts as away.
    /// <see langword="null"/> turns idle detection off.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:30", "04:00:00",
        ErrorMessage = "{0} must be between {1} and {2}, or null to turn idle detection off.")]
    public TimeSpan? IdleThreshold { get; set; } = TrackingOptions.Default.IdleThreshold;

    /// <summary>App keys that never go idle while in front, such as video players or meeting apps.</summary>
    public List<string> IdleExemptApps { get; set; } = [];

    /// <summary>A window has to stay in front at least this long to get its own interval.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:01:00", ErrorMessage = "{0} must be between {1} and {2}.")]
    public TimeSpan MinimumInterval { get; set; } = TrackingOptions.Default.MinimumInterval;

    /// <summary>How often the open interval is written out, bounding data loss on a crash.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00", ErrorMessage = "{0} must be between {1} and {2}.")]
    public TimeSpan CheckpointInterval { get; set; } = TrackingOptions.Default.CheckpointInterval;

    /// <summary>Whether window titles are stored. Applied by the privacy controls (#24).</summary>
    public bool RecordWindowTitles { get; set; } = true;

    /// <summary>App keys that are not recorded. Applied by the privacy controls (#24).</summary>
    public List<string> ExcludedApps { get; set; } = [];

    /// <summary>
    /// The user's app aliases and renames, applied on top of <see cref="AppAliasTable.BuiltIn"/>
    /// (see <see cref="AppAliasTable.WithOverrides"/>).
    /// </summary>
    public List<AppAliasSetting> AppAliases { get; set; } = [];

    /// <summary>Port of the local HTTP API (#27). Always bound to loopback.</summary>
    [Range(1024, 65535, ErrorMessage = "{0} must be between {1} and {2}.")]
    public int ApiPort { get; set; } = DefaultApiPort;

    /// <summary>
    /// Lowest level written to the log file. <c>Debug</c> adds tracking state changes (never window titles).
    /// </summary>
    [EnumDataType(typeof(LogLevel), ErrorMessage = "{0} must be one of Trace, Debug, Information, Warning, Error, Critical or None.")]
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>The tracking options these settings stand for, on top of <paramref name="baseline"/>.</summary>
    public TrackingOptions ToTrackingOptions(TrackingOptions baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        return baseline.WithIdleExemptApps(IdleExemptApps.Select(a => a.Trim())) with
        {
            IdleThreshold = IdleThreshold,
            MinimumInterval = MinimumInterval,
            CheckpointInterval = CheckpointInterval,
        };
    }

    /// <summary><paramref name="builtIn"/> with <see cref="AppAliases"/> applied.</summary>
    public AppAliasTable ToAliasTable(AppAliasTable builtIn)
    {
        ArgumentNullException.ThrowIfNull(builtIn);
        return builtIn.WithOverrides(AppAliases.Select(a => a.ToAlias()));
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (name, keys) in new[] { (nameof(IdleExemptApps), IdleExemptApps), (nameof(ExcludedApps), ExcludedApps) })
        {
            if (keys is null)
            {
                continue;
            }

            if (keys.Any(string.IsNullOrWhiteSpace))
            {
                yield return new ValidationResult($"{name} must not contain empty app keys.", [name]);
            }
        }

        if (AppAliases is null)
        {
            yield break;
        }

        // Check the merged table: a rename of a built-in app may have no executables of its own.
        foreach (var error in ToAliasTable(AppAliasTable.BuiltIn).Validate())
        {
            yield return new ValidationResult($"{nameof(AppAliases)}: {error}", [nameof(AppAliases)]);
        }
    }
}

/// <summary>One entry of <see cref="TimeglassSettings.AppAliases"/>; see <see cref="AppAlias"/>.</summary>
public sealed class AppAliasSetting
{
    /// <summary>Lowercase app key. An existing key renames that app; a new one adds an app.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Display name. Empty keeps the existing name when renaming.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Executable file names, e.g. <c>olk.exe</c>.</summary>
    public List<string> Exes { get; set; } = [];

    /// <summary>Optional regex on the window title.</summary>
    public string? TitlePattern { get; set; }

    /// <summary>The alias this entry stands for.</summary>
    public AppAlias ToAlias() => new(Key, Name, [.. Exes ?? []], string.IsNullOrEmpty(TitlePattern) ? null : TitlePattern);
}
