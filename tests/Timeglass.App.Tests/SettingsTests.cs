using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Timeglass.App.Settings;
using Timeglass.Core.Ports;
using Timeglass.Core.Settings;
using Timeglass.Core.Tracking;
using Timeglass.Storage.Settings;
using Timeglass.Testing;

namespace Timeglass.App.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly TempFolder _folder = new();

    // An in-memory source keeps values across a reload, so a test can change a value and reload.
    private readonly IConfigurationRoot _memory = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>())
        .Build();

    public void Dispose()
    {
        ((IDisposable)_memory).Dispose();
        _folder.Dispose();
    }

    private HostApplicationBuilder CreateBuilder()
    {
        var builder = TimeglassHost.CreateBuilder([]);
        builder.Configuration[$"{TimeglassOptions.Section}:{nameof(TimeglassOptions.DatabasePath)}"] = _folder.DatabasePath;
        builder.Configuration[$"{TimeglassOptions.Section}:{nameof(TimeglassOptions.SettingsPath)}"] = _folder.SettingsPath;
        builder.Configuration[$"{TimeglassOptions.Section}:{nameof(TimeglassOptions.LogDirectory)}"] = Path.Combine(_folder.Path, "logs");
        return builder;
    }

    /// <summary>The full host, reading its settings from <see cref="_memory"/> instead of the file.</summary>
    private IHost BuildHostWithMemorySettings()
    {
        var builder = CreateBuilder();
        builder.Services.AddSingleton(new SettingsConfiguration(_memory));
        return builder.Build();
    }

    private void Change(string key, string? value)
    {
        _memory[key] = value;
        _memory.Reload();
    }

    [Fact]
    public void Live_reload_propagates_to_the_engine_and_the_app_aliases()
    {
        using var host = BuildHostWithMemorySettings();
        var live = host.Services.GetRequiredService<LiveSettings>();
        var engine = host.Services.GetRequiredService<TrackingEngine>();
        var resolver = host.Services.GetRequiredService<IAppIdentityResolver>();
        engine.Options.IdleThreshold.ShouldBe(TimeSpan.FromMinutes(5));
        resolver.Resolve(TestWindows.Outlook()).DisplayName.ShouldBe("Outlook");

        Change(nameof(TimeglassSettings.IdleThreshold), "00:01:00");
        Change($"{nameof(TimeglassSettings.IdleExemptApps)}:0", "vlc");
        Change($"{nameof(TimeglassSettings.AppAliases)}:0:{nameof(AppAliasSetting.Key)}", "outlook");
        Change($"{nameof(TimeglassSettings.AppAliases)}:0:{nameof(AppAliasSetting.Name)}", "Mail");

        // TrackingService reads these before every event (see TrackingServiceTests).
        var options = live.GetTrackingOptions();
        options.IdleThreshold.ShouldBe(TimeSpan.FromMinutes(1));
        options.IdleExemptApps.ShouldContain("vlc");
        resolver.Resolve(TestWindows.Outlook()).DisplayName.ShouldBe("Mail");
        host.Services.GetRequiredService<IOptionsMonitor<TimeglassSettings>>().CurrentValue.IdleThreshold
            .ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void An_invalid_edit_is_logged_and_the_previous_settings_stay()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SettingsConfiguration(_memory));
        TimeglassHost.AddTimeglassSettings(services);
        using var provider = services.BuildServiceProvider();
        var log = new RecordingLogger<LiveSettings>();
        using var live = new LiveSettings(
            provider.GetRequiredService<IOptionsFactory<TimeglassSettings>>(),
            provider.GetRequiredService<IEnumerable<IOptionsChangeTokenSource<TimeglassSettings>>>(),
            () => log);

        Change(nameof(TimeglassSettings.ApiPort), "6000");
        Change(nameof(TimeglassSettings.ApiPort), "80");
        Change(nameof(TimeglassSettings.IdleThreshold), "five minutes");

        live.Current.Settings.ApiPort.ShouldBe(6000);
        live.Current.Tracking.IdleThreshold.ShouldBe(TimeSpan.FromMinutes(5));
        var warnings = log.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        warnings.Count.ShouldBe(2);
        warnings[0].ShouldContain("ApiPort must be between 1024 and 65535.");
        warnings[1].ShouldContain("IdleThreshold");
    }

    [Fact]
    public void Invalid_settings_stop_the_app_at_startup_with_a_clear_message()
    {
        _memory[nameof(TimeglassSettings.MinimumInterval)] = "00:05:00";

        var ex = Should.Throw<OptionsValidationException>(() =>
        {
            using var host = BuildHostWithMemorySettings();
            host.Start();
        });

        ex.Message.ShouldContain("MinimumInterval must be between 00:00:00 and 00:01:00.");
    }

    [Fact]
    public void A_missing_settings_file_is_created_with_the_defaults()
    {
        using var host = CreateBuilder().Build();
        host.Services.GetRequiredService<LiveSettings>();

        File.ReadAllText(_folder.SettingsPath).ShouldBe(SettingsFile.Serialize(new TimeglassSettings()));
    }

    [Fact]
    public void Settings_come_from_the_file()
    {
        SettingsFile.Save(_folder.SettingsPath, new TimeglassSettings { CheckpointInterval = TimeSpan.FromSeconds(42) });

        using var host = CreateBuilder().Build();

        host.Services.GetRequiredService<TrackingEngine>().Options.CheckpointInterval.ShouldBe(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public async Task Settings_saved_from_the_dashboard_are_written_to_the_file_and_applied()
    {
        using var host = CreateBuilder().Build();
        var store = host.Services.GetRequiredService<FileSettingsStore>();
        var live = host.Services.GetRequiredService<LiveSettings>();
        var updated = new TimeglassSettings { IdleThreshold = TimeSpan.FromMinutes(2), ExcludedApps = ["keepass"] };

        await store.SaveAsync(updated, TestContext.Current.CancellationToken);

        File.ReadAllText(_folder.SettingsPath).ShouldBe(SettingsFile.Serialize(updated));
        live.Current.Tracking.IdleThreshold.ShouldBe(TimeSpan.FromMinutes(2));
        store.Current.ExcludedApps.ShouldBe(["keepass"]);
    }

    [Fact]
    public async Task Settings_not_read_from_a_file_cannot_be_saved()
    {
        using var host = BuildHostWithMemorySettings();
        var store = host.Services.GetRequiredService<FileSettingsStore>();

        await Should.ThrowAsync<InvalidOperationException>(() => store.SaveAsync(new TimeglassSettings(), TestContext.Current.CancellationToken));
    }
}
