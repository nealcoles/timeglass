using Timeglass.Core.Domain;
using Timeglass.Core.Reporting;

namespace Timeglass.Core.Tests.Reporting;

public class AfterHoursReturnsTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
    private static readonly AppIdentity App = new("code", "Code");

    private static ActivityInterval Interval(IntervalKind kind, int start, int end, bool open = false) =>
        new(Guid.NewGuid(), kind, Day.AddMinutes(start), Day.AddMinutes(end), open,
            kind == IntervalKind.Active ? App : null, null, null);

    [Fact]
    public void Excludes_the_entire_absence_and_multiple_intervals_of_the_final_return()
    {
        var work = Interval(IntervalKind.Active, 540, 990);
        var away = Interval(IntervalKind.Away, 990, 1200);
        var return1 = Interval(IntervalKind.Active, 1200, 1205);
        var return2 = Interval(IntervalKind.Active, 1205, 1210);

        AfterHoursReturns.Filter([work, away, return1, return2], TimeZoneInfo.Utc, WorkHours.Default,
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(10)).ShouldBe([work]);
    }

    [Theory]
    [InlineData(960, 1019, 1020, false)] // absence too short
    [InlineData(900, 960, 965, false)] // return during work hours
    [InlineData(990, 1200, 1211, false)] // return too long
    [InlineData(990, 1200, 1205, true)] // return still in progress
    public void Keeps_nonqualifying_intervals(int start, int resumed, int end, bool open)
    {
        var intervals = new[] { Interval(IntervalKind.Away, start, resumed), Interval(IntervalKind.Active, resumed, end, open) };

        AfterHoursReturns.Filter(intervals, TimeZoneInfo.Utc, WorkHours.Default,
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(10)).ShouldBe(intervals);
    }

    [Fact]
    public void Keeps_a_short_return_followed_by_more_work()
    {
        var intervals = new[]
        {
            Interval(IntervalKind.Away, 990, 1200),
            Interval(IntervalKind.Active, 1200, 1205),
            Interval(IntervalKind.Away, 1205, 1230),
            Interval(IntervalKind.Active, 1230, 1260),
        };

        AfterHoursReturns.Filter(intervals, TimeZoneInfo.Utc, WorkHours.Default,
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(10)).ShouldBe(intervals);
    }

    [Fact]
    public void Uses_configured_limits_and_local_work_hours()
    {
        var oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
        var intervals = new[]
        {
            Interval(IntervalKind.Away, 930, 990),
            Interval(IntervalKind.Active, 990, 1002),
        };
        var hours = new WorkHours(TimeSpan.FromHours(9), TimeSpan.FromHours(17));

        AfterHoursReturns.Filter(intervals, oslo, hours, TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(15)).ShouldBeEmpty();
        AfterHoursReturns.Filter(intervals, oslo, hours, TimeSpan.FromMinutes(61), TimeSpan.FromMinutes(15)).ShouldBe(intervals);
        AfterHoursReturns.Filter(intervals, oslo, hours, TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(11)).ShouldBe(intervals);
    }

    [Fact]
    public void Excludes_an_absence_that_started_on_an_earlier_day()
    {
        var intervals = new[]
        {
            Interval(IntervalKind.Away, -60, 1200),
            Interval(IntervalKind.Active, 1200, 1205),
        };

        AfterHoursReturns.Filter(intervals, TimeZoneInfo.Utc, WorkHours.Default,
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(10)).ShouldBeEmpty();
    }
}
