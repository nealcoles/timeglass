using System.Globalization;
using Timeglass.Core.Reporting;

namespace Timeglass.Core.Tests.Reporting;

public class WorkHoursTests
{
    // Oslo is UTC+1 in winter and UTC+2 in summer; the clocks go forward on 29 March 2026.
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    private static readonly DateOnly Wednesday = new(2026, 11, 4);

    private static DateTimeOffset Local(string at) =>
        DateTimeOffset.Parse(at, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2026-11-04T08:15+01:00", "2026-11-04T18:30+01:00", 8 * 60)] // around work hours: all of them
    [InlineData("2026-11-04T10:00+01:00", "2026-11-04T15:30+01:00", 5 * 60 + 30)] // inside: first to last
    [InlineData("2026-11-04T08:00+01:00", "2026-11-04T12:00+01:00", 3 * 60)] // early start is cut
    [InlineData("2026-11-04T13:00+01:00", "2026-11-05T01:00+01:00", 4 * 60)] // late finish is cut
    [InlineData("2026-11-04T06:00+01:00", "2026-11-04T08:59+01:00", 0)] // only before work hours
    [InlineData("2026-11-04T17:00+01:00", "2026-11-04T22:00+01:00", 0)] // only after work hours
    public void Is_the_span_from_first_to_last_activity_inside_work_hours(string first, string last, int minutes)
    {
        WorkHours.Default.Within(Wednesday, Local(first), Local(last), Oslo)
            .ShouldBe(TimeSpan.FromMinutes(minutes));
    }

    [Fact]
    public void Is_zero_without_activity()
    {
        WorkHours.Default.Within(Wednesday, null, null, Oslo).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Uses_the_configured_hours()
    {
        var hours = new WorkHours(TimeSpan.FromHours(7.5), TimeSpan.FromHours(15.5));

        hours.Within(Wednesday, Local("2026-11-04T07:00+01:00"), Local("2026-11-04T16:00+01:00"), Oslo)
            .ShouldBe(TimeSpan.FromHours(8));
    }

    [Fact]
    public void Counts_real_time_on_the_day_the_clocks_go_forward()
    {
        // 01:00 to 04:00 local on 29 March is two real hours: 02:00 to 03:00 doesn't exist.
        var night = new WorkHours(TimeSpan.FromHours(1), TimeSpan.FromHours(4));

        night.Within(new DateOnly(2026, 3, 29), Local("2026-03-29T00:00+01:00"), Local("2026-03-29T12:00+02:00"), Oslo)
            .ShouldBe(TimeSpan.FromHours(2));
    }
}
