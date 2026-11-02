using Timeglass.Core.Apps;
using Timeglass.Core.Domain;
using Timeglass.Core.Tracking;
using Timeglass.Testing;
using static Timeglass.Testing.TestWindows;

namespace Timeglass.Core.Tests.Tracking;

public class TrackingEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private readonly TrackingEngine _engine = new(new AliasAppIdentityResolver(AppAliasTable.Empty));
    private readonly IntervalTimeline _timeline = new();

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private IReadOnlyList<IntervalChange> Send(TrackingEvent e)
    {
        var changes = _engine.Handle(e);
        _timeline.Apply(changes);
        return changes;
    }

    private void Start(WindowSnapshot? window, double at = 0) => Send(new TrackingStarted(At(at), window));

    private void Switch(WindowSnapshot? window, double at) => Send(new ForegroundChanged(At(at), window));

    private void Tick(double at) => Send(new Tick(At(at), At(at)));

    private void Stop(double at) => Send(new TrackingStopped(At(at)));

    private static string? Key(ActivityInterval i) => i.App?.Key;

    [Fact]
    public void Start_opens_an_active_interval_for_the_current_window()
    {
        var changes = _engine.Handle(new TrackingStarted(At(0), Outlook()));

        var opened = changes.ShouldHaveSingleItem();
        opened.Kind.ShouldBe(IntervalChangeKind.Opened);
        opened.Interval.Kind.ShouldBe(IntervalKind.Active);
        opened.Interval.IsOpen.ShouldBeTrue();
        opened.Interval.Start.ShouldBe(At(0));
        opened.Interval.App.ShouldBe(new AppIdentity("olk", "Outlook"));
        opened.Interval.Title.ShouldBe("Inbox");
        _engine.IsTracking.ShouldBeTrue();
    }

    [Fact]
    public void Events_before_start_are_ignored()
    {
        _engine.Handle(new ForegroundChanged(At(0), Outlook())).ShouldBeEmpty();
        _engine.Handle(new Tick(At(1), At(1))).ShouldBeEmpty();
        _engine.Handle(new TrackingStopped(At(2))).ShouldBeEmpty();
    }

    [Fact]
    public void Switching_windows_produces_contiguous_intervals_backdated_to_the_switch()
    {
        Start(Outlook());
        Switch(Code(), at: 60);
        Tick(at: 62);
        Switch(Browser(), at: 120);
        Stop(at: 180);

        var intervals = _timeline.Intervals;
        intervals.Select(Key).ShouldBe(["olk", "code", "firefox"]);
        intervals.Select(i => (i.Start, i.End)).ShouldBe(
        [
            (At(0), At(60)),
            (At(60), At(120)),
            (At(120), At(180)),
        ]);
        intervals.ShouldAllBe(i => !i.IsOpen);
    }

    [Fact]
    public void A_switch_is_pending_until_the_minimum_interval_has_passed()
    {
        Start(Outlook());

        Send(new ForegroundChanged(At(10), Code())).ShouldBeEmpty();
        Send(new Tick(At(10.5), At(10.5))).ShouldBeEmpty();

        var changes = Send(new Tick(At(11), At(11)));

        changes.Select(c => c.Kind).ShouldBe([IntervalChangeKind.Closed, IntervalChangeKind.Opened]);
        changes[0].Interval.End.ShouldBe(At(10));
        changes[1].Interval.Start.ShouldBe(At(10));
    }

    [Fact]
    public void Alt_tab_flicker_shorter_than_the_minimum_counts_towards_the_previous_window()
    {
        Start(Outlook());
        Switch(Teams(), at: 30);
        Switch(Code(), at: 30.4);
        Tick(at: 32);
        Stop(at: 60);

        var intervals = _timeline.Intervals;
        intervals.Select(Key).ShouldBe(["olk", "code"]);
        intervals[0].End.ShouldBe(At(30.4));
        intervals[1].Start.ShouldBe(At(30.4));
    }

    [Fact]
    public void Flicking_away_and_back_keeps_a_single_interval()
    {
        Start(Outlook());
        Switch(Teams(), at: 30);
        Switch(Outlook(), at: 30.3);
        Tick(at: 35);
        Stop(at: 60);

        var interval = _timeline.Intervals.ShouldHaveSingleItem();
        Key(interval).ShouldBe("olk");
        interval.Start.ShouldBe(At(0));
        interval.End.ShouldBe(At(60));
    }

    [Fact]
    public void Repeated_events_for_the_same_window_are_merged()
    {
        Start(Outlook());
        Switch(Outlook(), at: 10);
        Switch(Outlook(), at: 20);
        Tick(at: 30);
        Stop(at: 40);

        _timeline.Intervals.ShouldHaveSingleItem().Duration.ShouldBe(TimeSpan.FromSeconds(40));
    }

    [Fact]
    public void Duplicate_events_for_the_pending_window_keep_the_original_switch_time()
    {
        Start(Outlook());
        Switch(Code(), at: 10);
        Switch(Code(), at: 10.8);
        Tick(at: 11);

        _timeline.Intervals.Select(i => i.Start).ShouldBe([At(0), At(10)]);
    }

    [Fact]
    public void A_title_change_in_the_same_app_starts_a_new_interval()
    {
        Start(Outlook("Inbox"));
        Switch(Outlook("RE: Budget"), at: 30);
        Stop(at: 60);

        _timeline.Intervals.Select(i => i.Title).ShouldBe(["Inbox", "RE: Budget"]);
    }

    [Fact]
    public void No_foreground_window_is_tracked_as_unknown()
    {
        Start(null);
        Stop(at: 5);

        _timeline.Intervals.ShouldHaveSingleItem().App.ShouldBe(AppIdentity.Unknown);
    }

    [Fact]
    public void A_switch_right_at_start_replaces_the_empty_interval_instead_of_emitting_it()
    {
        Start(Outlook());
        Switch(Code(), at: 0);
        var changes = Send(new Tick(At(2), At(2)));

        var updated = changes.ShouldHaveSingleItem();
        updated.Kind.ShouldBe(IntervalChangeKind.Updated);
        Key(updated.Interval).ShouldBe("code");
        _timeline.Intervals.ShouldHaveSingleItem();
    }

    [Fact]
    public void Ticks_checkpoint_the_open_interval_at_the_configured_interval()
    {
        _engine.Options = TrackingOptions.Default with { CheckpointInterval = TimeSpan.FromSeconds(15) };
        Start(Outlook());

        Send(new Tick(At(10), At(10))).ShouldBeEmpty();
        var checkpoint = Send(new Tick(At(15), At(15))).ShouldHaveSingleItem();
        Send(new Tick(At(20), At(20))).ShouldBeEmpty();

        checkpoint.Kind.ShouldBe(IntervalChangeKind.Updated);
        checkpoint.Interval.End.ShouldBe(At(15));
        checkpoint.Interval.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void Stop_commits_a_due_pending_switch_and_closes_everything()
    {
        Start(Outlook());
        Switch(Code(), at: 10);
        Stop(at: 20);

        _timeline.Intervals.Select(Key).ShouldBe(["olk", "code"]);
        _timeline.Intervals.ShouldAllBe(i => !i.IsOpen);
        _engine.IsTracking.ShouldBeFalse();
        _engine.Current.ShouldBeNull();
    }

    [Fact]
    public void Stop_drops_a_pending_switch_that_is_not_due_yet()
    {
        Start(Outlook());
        Switch(Code(), at: 19.5);
        Stop(at: 20);

        var interval = _timeline.Intervals.ShouldHaveSingleItem();
        Key(interval).ShouldBe("olk");
        interval.End.ShouldBe(At(20));
    }

    [Fact]
    public void Timestamps_going_backwards_are_clamped()
    {
        Start(Outlook());
        Switch(Code(), at: 30);
        Tick(at: 32);
        Switch(Browser(), at: 20); // clock went back
        Tick(at: 40);
        Stop(at: 50);

        var intervals = _timeline.Intervals;
        intervals.ShouldAllBe(i => i.End >= i.Start);
        intervals.Zip(intervals.Skip(1)).ShouldAllBe(p => p.First.End == p.Second.Start);
    }

    [Fact]
    public void Starting_again_while_tracking_closes_the_open_interval_first()
    {
        Start(Outlook());
        Start(Code(), at: 30);
        Stop(at: 60);

        _timeline.Intervals.Select(Key).ShouldBe(["olk", "code"]);
        _timeline.Intervals[0].End.ShouldBe(At(30));
    }

    [Fact]
    public void Option_changes_apply_to_the_next_event()
    {
        Start(Outlook());
        Switch(Code(), at: 10);
        _engine.Options = TrackingOptions.Default with { MinimumInterval = TimeSpan.FromSeconds(5) };

        Send(new Tick(At(12), At(12))).ShouldBeEmpty();
        Send(new Tick(At(15), At(15))).ShouldNotBeEmpty();
    }

    [Fact]
    public void Interval_ids_come_from_the_id_factory()
    {
        var ids = new Queue<Guid>([Guid.Parse("00000000-0000-0000-0000-000000000001")]);
        var engine = new TrackingEngine(new AliasAppIdentityResolver(AppAliasTable.Empty), newId: _ => ids.Dequeue());

        var opened = engine.Handle(new TrackingStarted(At(0), Outlook())).ShouldHaveSingleItem();

        opened.Interval.Id.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    }
}
