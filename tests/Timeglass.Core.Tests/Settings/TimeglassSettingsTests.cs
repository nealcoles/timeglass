using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Timeglass.Core.Apps;
using Timeglass.Core.Reporting;
using Timeglass.Core.Settings;
using Timeglass.Core.Tracking;

namespace Timeglass.Core.Tests.Settings;

public class TimeglassSettingsTests
{
    /// <summary>Validates like <c>ValidateDataAnnotations</c> does: every property, then <see cref="IValidatableObject"/>.</summary>
    private static List<string> Errors(TimeglassSettings settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        return [.. results.Select(r => r.ErrorMessage ?? string.Empty)];
    }

    [Fact]
    public void Defaults_are_valid()
    {
        Errors(new TimeglassSettings()).ShouldBeEmpty();
    }

    [Fact]
    public void Defaults_match_the_engine_defaults()
    {
        var options = new TimeglassSettings().ToTrackingOptions(TrackingOptions.Default);

        options.ShouldBe(TrackingOptions.Default with
        {
            IdleExemptApps = options.IdleExemptApps,
            ExcludedApps = options.ExcludedApps,
            AppsWithoutTitles = options.AppsWithoutTitles,
        });
        options.IdleExemptApps.ShouldBeEmpty();
        options.ExcludedApps.ShouldBeEmpty();
        options.AppsWithoutTitles.ShouldBeEmpty();
    }

    [Fact]
    public void Privacy_settings_map_to_the_engine_options()
    {
        var options = new TimeglassSettings
        {
            RecordWindowTitles = false,
            ExcludedApps = [" keepass "],
            AppsWithoutTitles = ["Outlook"],
        }.ToTrackingOptions(TrackingOptions.Default);

        options.RecordWindowTitles.ShouldBeFalse();
        options.ExcludedApps.ShouldBe(["keepass"]);
        options.AppsWithoutTitles.Contains("outlook").ShouldBeTrue();
    }

    [Fact]
    public void An_app_cannot_be_both_excluded_and_without_titles()
    {
        var errors = Errors(new TimeglassSettings { ExcludedApps = ["keepass", "outlook"], AppsWithoutTitles = ["Outlook "] });

        errors.ShouldBe(["outlook cannot be in both ExcludedApps and AppsWithoutTitles; pick one."]);
    }

    [Fact]
    public void Everything_is_kept_by_default()
    {
        new TimeglassSettings().RetentionMonths.ShouldBeNull();
        Errors(new TimeglassSettings { RetentionMonths = 12 }).ShouldBeEmpty();
    }

    [Fact]
    public void Idle_detection_can_be_turned_off()
    {
        Errors(new TimeglassSettings { IdleThreshold = null }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(nameof(TimeglassSettings.IdleThreshold), "IdleThreshold must be between 00:00:30 and 04:00:00, or null to turn idle detection off.")]
    [InlineData(nameof(TimeglassSettings.MinimumInterval), "MinimumInterval must be between 00:00:00 and 00:01:00.")]
    [InlineData(nameof(TimeglassSettings.CheckpointInterval), "CheckpointInterval must be between 00:00:01 and 00:10:00.")]
    [InlineData(nameof(TimeglassSettings.ApiPort), "ApiPort must be between 1024 and 65535.")]
    [InlineData(nameof(TimeglassSettings.LogLevel), "LogLevel must be one of Trace, Debug, Information, Warning, Error, Critical or None.")]
    [InlineData(nameof(TimeglassSettings.RetentionMonths), "RetentionMonths must be between 1 and 120, or null to keep everything.")]
    public void Out_of_range_values_are_rejected_with_a_clear_message(string property, string message)
    {
        var settings = new TimeglassSettings();
        switch (property)
        {
            case nameof(TimeglassSettings.RetentionMonths): settings.RetentionMonths = 0; break;
            case nameof(TimeglassSettings.IdleThreshold): settings.IdleThreshold = TimeSpan.FromSeconds(10); break;
            case nameof(TimeglassSettings.MinimumInterval): settings.MinimumInterval = TimeSpan.FromSeconds(-1); break;
            case nameof(TimeglassSettings.CheckpointInterval): settings.CheckpointInterval = TimeSpan.Zero; break;
            case nameof(TimeglassSettings.ApiPort): settings.ApiPort = 80; break;
            case nameof(TimeglassSettings.LogLevel): settings.LogLevel = (LogLevel)42; break;
        }

        Errors(settings).ShouldBe([message]);
    }

    [Fact]
    public void Empty_app_keys_are_rejected()
    {
        var errors = Errors(new TimeglassSettings { IdleExemptApps = ["vlc", " "], ExcludedApps = [""], AppsWithoutTitles = ["\t"] });

        errors.ShouldBe(
        [
            "IdleExemptApps must not contain empty app keys.",
            "ExcludedApps must not contain empty app keys.",
            "AppsWithoutTitles must not contain empty app keys.",
        ]);
    }

    [Fact]
    public void Invalid_app_aliases_are_rejected_with_the_alias_table_errors()
    {
        var errors = Errors(new TimeglassSettings
        {
            AppAliases =
            [
                new AppAliasSetting { Key = "Not A Key", Name = "Broken", Exes = ["broken.exe"] },
                new AppAliasSetting { Key = "empty", Name = "No executables" },
            ],
        });

        errors.ShouldBe(
        [
            "AppAliases: 'Not A Key': key must be lowercase letters, digits and '-'.",
            "AppAliases: 'empty': needs at least one executable.",
        ]);
    }

    [Fact]
    public void A_rename_needs_no_executables_of_its_own()
    {
        var settings = new TimeglassSettings { AppAliases = [new AppAliasSetting { Key = "outlook", Name = "Mail" }] };

        Errors(settings).ShouldBeEmpty();
        settings.ToAliasTable(AppAliasTable.BuiltIn).Find("olk.exe", title: null)!.Name.ShouldBe("Mail");
    }

    [Fact]
    public void Taking_every_executable_of_a_built_in_app_is_valid()
    {
        var settings = new TimeglassSettings
        {
            AppAliases = [new AppAliasSetting { Key = "office", Name = "Office", Exes = ["winword.exe", "excel.exe"] }],
        };

        Errors(settings).ShouldBeEmpty();
        var table = settings.ToAliasTable(AppAliasTable.BuiltIn);
        table.Find("winword.exe", title: null)!.Key.ShouldBe("office");
        table.Aliases.ShouldNotContain(a => a.Key == "word");
    }

    [Fact]
    public void Maps_to_tracking_options()
    {
        var settings = new TimeglassSettings
        {
            IdleThreshold = TimeSpan.FromMinutes(2),
            IdleExemptApps = ["VLC"],
            MinimumInterval = TimeSpan.FromSeconds(5),
            CheckpointInterval = TimeSpan.FromMinutes(1),
        };

        var options = settings.ToTrackingOptions(TrackingOptions.Default with { TickInterval = TimeSpan.FromSeconds(2) });

        options.IdleThreshold.ShouldBe(TimeSpan.FromMinutes(2));
        options.IdleExemptApps.ShouldContain("vlc");
        options.MinimumInterval.ShouldBe(TimeSpan.FromSeconds(5));
        options.CheckpointInterval.ShouldBe(TimeSpan.FromMinutes(1));
        options.TickInterval.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Work_hours_default_to_nine_to_five()
    {
        new TimeglassSettings().ToWorkHours().ShouldBe(new WorkHours(TimeSpan.FromHours(9), TimeSpan.FromHours(17)));
    }

    [Theory]
    [InlineData("17:00", "09:00")]
    [InlineData("09:00", "09:00")]
    public void Work_hours_must_end_after_they_start(string start, string end)
    {
        var settings = new TimeglassSettings { WorkHoursStart = TimeSpan.Parse(start, CultureInfo.InvariantCulture), WorkHoursEnd = TimeSpan.Parse(end, CultureInfo.InvariantCulture) };

        Errors(settings).ShouldContain("WorkHoursEnd must be after WorkHoursStart.");
    }

    [Fact]
    public void Work_hours_must_be_a_time_of_day()
    {
        Errors(new TimeglassSettings { WorkHoursEnd = TimeSpan.FromHours(25) })
            .ShouldContain(e => e.StartsWith("WorkHoursEnd must be between", StringComparison.Ordinal));
    }
}
