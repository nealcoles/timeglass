using Timeglass.App.Tray;

namespace Timeglass.App.Tests;

public class TrayTextTests
{
    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(59.9, "0 min")]
    [InlineData(45 * 60, "45 min")]
    [InlineData((3 * 60 + 5) * 60, "3 h 05 min")]
    [InlineData(25 * 3600, "25 h 00 min")]
    [InlineData(-30, "0 min")]
    public void Formats_durations_in_whole_minutes(double seconds, string expected) =>
        TrayText.Duration(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);

    [Fact]
    public void Tooltip_shows_todays_active_time() =>
        TrayText.Tooltip(TimeSpan.FromMinutes(95), paused: false).ShouldBe("Timeglass: 1 h 35 min active today");

    [Fact]
    public void Tooltip_says_when_tracking_is_paused() =>
        TrayText.Tooltip(TimeSpan.FromMinutes(5), paused: true).ShouldBe("Timeglass: 5 min active today (paused)");

    [Fact]
    public void Tooltip_before_the_first_query_is_just_the_name() =>
        TrayText.Tooltip(null, paused: false).ShouldBe("Timeglass");

    [Fact]
    public void Tooltip_fits_the_notification_area_limit() =>
        TrayText.Tooltip(TimeSpan.MaxValue, paused: true).Length.ShouldBeLessThanOrEqualTo(TrayText.MaxTooltipLength);
}
