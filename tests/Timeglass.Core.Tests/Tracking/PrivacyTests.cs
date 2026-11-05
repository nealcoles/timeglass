using Timeglass.Core.Apps;
using Timeglass.Core.Domain;
using Timeglass.Core.Ports;
using Timeglass.Core.Tracking;
using Timeglass.Testing;
using static Timeglass.Testing.TestWindows;

namespace Timeglass.Core.Tests.Tracking;

public class PrivacyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private readonly TrackingEngine _engine = new(
        new AliasAppIdentityResolver(AppAliasTable.Empty),
        TrackingOptions.Default with
        {
            CheckpointInterval = TimeSpan.FromDays(1), // keep checkpoints out of these tests
            ClockJumpThreshold = null,                 // events here are minutes apart
        });

    private readonly IntervalTimeline _timeline = new();
    private readonly List<IntervalChange> _changes = [];

    private static DateTimeOffset At(double minutes) => T0.AddMinutes(minutes);

    private void Send(TrackingEvent e)
    {
        var changes = _engine.Handle(e);
        _changes.AddRange(changes);
        _timeline.Apply(changes);
    }

    private void Start(WindowSnapshot? window, double at = 0) => Send(new TrackingStarted(At(at), window));

    private void Switch(WindowSnapshot? window, double at) => Send(new ForegroundChanged(At(at), window));

    private void Tick(double at, double? lastInput = null) => Send(new Tick(At(at), At(lastInput ?? at)));

    private void Stop(double at) => Send(new TrackingStopped(At(at)));

    private void Options(Func<TrackingOptions, TrackingOptions> change) => _engine.Options = change(_engine.Options);

    private IReadOnlyList<(IntervalKind Kind, string? App, DateTimeOffset Start, DateTimeOffset End)> Summary() =>
        [.. _timeline.Intervals.Select(i => (i.Kind, i.App?.Key, i.Start, i.End))];

    [Fact]
    public void An_excluded_app_leaves_a_gap()
    {
        Options(o => o.WithExcludedApps("olk"));
        Start(Code());
        Switch(Outlook(), at: 10);
        Tick(at: 11);
        Switch(Browser(), at: 20);
        Tick(at: 21);
        Stop(at: 30);

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "code", At(0), At(10)),
            (IntervalKind.Active, "firefox", At(20), At(30)),
        ]);
    }

    [Fact]
    public void Starting_in_an_excluded_app_opens_nothing_until_a_tracked_app_comes_to_the_front()
    {
        Options(o => o.WithExcludedApps("olk"));
        Start(Outlook());
        _engine.Current.ShouldBeNull();
        _engine.IsTracking.ShouldBeTrue();

        Switch(Code(), at: 5);
        Tick(at: 6);

        _engine.Current.ShouldNotBeNull().App!.Key.ShouldBe("code");
        _engine.Current.Start.ShouldBe(At(5));
    }

    [Fact]
    public void Excluded_apps_are_matched_case_insensitively()
    {
        Options(o => o.WithExcludedApps("OLK"));
        Start(Outlook());

        _engine.Current.ShouldBeNull();
    }

    [Fact]
    public void Flicking_through_an_excluded_app_keeps_the_current_interval()
    {
        Options(o => o.WithExcludedApps("olk"));
        Start(Code());
        Switch(Outlook(), at: 10);
        Switch(Code(), at: 10.005); // 0.3 s later
        Stop(at: 20);

        Summary().ShouldBe([(IntervalKind.Active, "code", At(0), At(20))]);
    }

    [Fact]
    public void Flicking_out_of_an_excluded_app_records_nothing()
    {
        Options(o => o.WithExcludedApps("olk"));
        Start(Outlook());
        Switch(Code(), at: 10);
        Switch(Outlook(), at: 10.005);
        Tick(at: 11);
        Stop(at: 20);

        _changes.ShouldBeEmpty();
    }

    [Fact]
    public void Title_changes_in_an_excluded_app_record_nothing()
    {
        Options(o => o.WithExcludedApps("olk"));
        Start(Outlook("Inbox"));
        Switch(Outlook("RE: Salary review"), at: 1);
        Tick(at: 2);
        Stop(at: 3);

        _changes.ShouldBeEmpty();
    }

    [Fact]
    public void An_excluded_app_does_not_go_idle()
    {
        Options(o => o.WithExcludedApps("olk") with { IdleThreshold = TimeSpan.FromMinutes(5) });
        Start(Code());
        Switch(Outlook(), at: 10);
        Tick(at: 11);
        Tick(at: 30, lastInput: 11); // 19 minutes without input
        Switch(Browser(), at: 40);
        Tick(at: 41);

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "code", At(0), At(10)),
            (IntervalKind.Active, "firefox", At(40), At(40)),
        ]);
    }

    [Fact]
    public void Locking_in_an_excluded_app_is_away_and_unlocking_back_into_it_records_nothing()
    {
        Options(o => o.WithExcludedApps("olk"));
        Start(Outlook());
        Send(new SessionChanged(At(10), SessionEvent.Locked));
        Send(new SessionChanged(At(20), SessionEvent.Unlocked, Outlook()));
        Stop(at: 30);

        Summary().ShouldBe([(IntervalKind.Away, null, At(10), At(20))]);
    }

    [Fact]
    public void Excluding_the_app_in_front_closes_its_interval_without_the_title()
    {
        Start(Outlook("RE: Salary review"));
        Tick(at: 5);

        Options(o => o.WithExcludedApps("olk"));
        Tick(at: 6);

        var closed = _timeline.Intervals.ShouldHaveSingleItem();
        closed.IsOpen.ShouldBeFalse();
        closed.End.ShouldBe(At(6));
        closed.Title.ShouldBeNull();
        _engine.Current.ShouldBeNull();
    }

    [Fact]
    public void Including_the_app_in_front_again_opens_an_interval_for_it()
    {
        Options(o => o.WithExcludedApps("olk"));
        Start(Outlook());
        Tick(at: 5);

        Options(o => o.WithExcludedApps());
        Tick(at: 6);

        _engine.Current.ShouldNotBeNull().App!.Key.ShouldBe("olk");
        _engine.Current.Start.ShouldBe(At(6));
    }

    [Fact]
    public void Titles_are_not_recorded_when_turned_off()
    {
        Options(o => o with { RecordWindowTitles = false });
        Start(Outlook());
        Switch(Code(), at: 10);
        Tick(at: 11);
        Stop(at: 20);

        _changes.ShouldNotBeEmpty();
        _changes.ShouldAllBe(c => c.Interval.Title == null);
        _changes.ShouldAllBe(c => c.Interval.ExePath != null);
    }

    [Fact]
    public void An_app_without_titles_is_tracked_without_them()
    {
        Options(o => o.WithAppsWithoutTitles("olk"));
        Start(Outlook("RE: Salary review"));
        Switch(Code(), at: 10);
        Tick(at: 11);
        Stop(at: 20);

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "olk", At(0), At(10)),
            (IntervalKind.Active, "code", At(10), At(20)),
        ]);
        _timeline.Intervals[0].Title.ShouldBeNull();
        _timeline.Intervals[1].Title.ShouldBe("Program.cs - timeglass");
        _changes.Where(c => c.Interval.App?.Key == "olk").ShouldAllBe(c => c.Interval.Title == null);
    }

    [Fact]
    public void Title_changes_in_an_app_without_titles_are_not_window_changes()
    {
        Options(o => o.WithAppsWithoutTitles("olk"));
        Start(Outlook("Inbox"));
        Switch(Outlook("RE: Salary review"), at: 5);
        Tick(at: 6);
        Stop(at: 10);

        Summary().ShouldBe([(IntervalKind.Active, "olk", At(0), At(10))]);
    }

    [Fact]
    public void Turning_titles_off_removes_the_title_from_the_open_interval()
    {
        Start(Outlook("RE: Salary review"));
        Tick(at: 5);

        Options(o => o with { RecordWindowTitles = false });
        Tick(at: 6);

        var updated = _changes[^1];
        updated.Kind.ShouldBe(IntervalChangeKind.Updated);
        updated.Interval.Title.ShouldBeNull();
        updated.Interval.Start.ShouldBe(At(0));
    }
}
