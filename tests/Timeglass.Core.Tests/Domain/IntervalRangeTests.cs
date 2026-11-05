using Timeglass.Core.Domain;

namespace Timeglass.Core.Tests.Domain;

public class IntervalRangeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid NewId = Guid.Parse("0196f3b2-0000-7000-8000-000000000002");

    private static DateTimeOffset At(double minutes) => T0.AddMinutes(minutes);

    private static ActivityInterval Interval(double start, double end) =>
        new(Guid.Parse("0196f3b2-0000-7000-8000-000000000001"), IntervalKind.Active, At(start), At(end), false,
            new AppIdentity("olk", "Outlook"), @"C:\olk.exe", "RE: Salary review");

    private static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Delete(ActivityInterval interval, double from, double to) =>
        [.. IntervalRange.Delete(interval, At(from), At(to), _ => NewId).Select(i => (i.Start, i.End))];

    [Theory]
    [InlineData(0, 10)]  // exactly the range
    [InlineData(2, 8)]   // inside it
    [InlineData(0, 0)]   // empty, at the range start
    [InlineData(5, 5)]   // empty, inside
    public void An_interval_inside_the_range_is_deleted(double start, double end)
    {
        Delete(Interval(start, end), 0, 10).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(-10, 0)]  // ends where the range starts
    [InlineData(10, 20)]  // starts where it ends
    [InlineData(10, 10)]  // empty, at the range end
    [InlineData(30, 40)]
    public void An_interval_outside_the_range_is_kept_as_it_is(double start, double end)
    {
        var interval = Interval(start, end);

        IntervalRange.Delete(interval, At(0), At(10), _ => NewId).ShouldBe([interval]);
    }

    [Fact]
    public void An_interval_overlapping_the_start_of_the_range_is_cut_at_the_start()
    {
        var piece = IntervalRange.Delete(Interval(-5, 5), At(0), At(10), _ => NewId).ShouldHaveSingleItem();

        (piece.Start, piece.End).ShouldBe((At(-5), At(0)));
        piece.Id.ShouldBe(Interval(0, 0).Id);
    }

    [Fact]
    public void An_interval_overlapping_the_end_of_the_range_starts_at_the_end()
    {
        var piece = IntervalRange.Delete(Interval(5, 15), At(0), At(10), _ => NewId).ShouldHaveSingleItem();

        (piece.Start, piece.End).ShouldBe((At(10), At(15)));
        piece.Id.ShouldBe(Interval(0, 0).Id);
    }

    [Fact]
    public void An_interval_spanning_the_range_is_split_in_two()
    {
        var pieces = IntervalRange.Delete(Interval(-5, 15), At(0), At(10), _ => NewId);

        pieces.Select(p => (p.Start, p.End)).ShouldBe([(At(-5), At(0)), (At(10), At(15))]);
        pieces[0].Id.ShouldBe(Interval(0, 0).Id);
        pieces[1].Id.ShouldBe(NewId);
        pieces.ShouldAllBe(p => p.Title == "RE: Salary review" && p.App!.Key == "olk");
    }

    [Fact]
    public void An_empty_range_deletes_nothing()
    {
        Delete(Interval(0, 10), 5, 5).ShouldBe([(At(0), At(10))]);
    }

    [Fact]
    public void A_range_that_ends_before_it_starts_is_rejected()
    {
        Should.Throw<ArgumentException>(() => IntervalRange.Delete(Interval(0, 10), At(5), At(4), _ => NewId));
    }
}
