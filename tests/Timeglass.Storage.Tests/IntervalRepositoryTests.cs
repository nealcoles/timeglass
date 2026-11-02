using Dapper;
using Timeglass.Core.Apps;
using Timeglass.Core.Domain;
using Timeglass.Core.Tracking;
using Timeglass.Testing;
using static Timeglass.Testing.TestWindows;

namespace Timeglass.Storage.Tests;

public sealed class IntervalRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly AppIdentity OutlookApp = new("outlook", "Outlook");

    private readonly TempDatabase _temp = new();
    private TimeglassDatabase _database = null!;
    private IntervalRepository _repository = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _database = _temp.Open();
        _repository = new IntervalRepository(_database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _repository.DisposeAsync();
        _database.Dispose();
        _temp.Dispose();
    }

    private static DateTimeOffset At(double minutes) => T0.AddMinutes(minutes);

    private static ActivityInterval Active(double start, double end, bool open = false, AppIdentity? app = null) =>
        new(Guid.CreateVersion7(), IntervalKind.Active, At(start), At(end), open, app ?? OutlookApp, @"C:\olk.exe", "Inbox");

    private static ActivityInterval Away(double start, double end) =>
        new(Guid.CreateVersion7(), IntervalKind.Away, At(start), At(end), false, null, null, null);

    private async Task StoreAsync(params ActivityInterval[] intervals)
    {
        await _repository.ApplyAsync(
            [.. intervals.Select(i => new IntervalChange(IntervalChangeKind.Closed, i))], Ct);
        await _repository.FlushAsync(Ct);
    }

    private Task<IReadOnlyList<ActivityInterval>> RangeAsync(double from, double to) =>
        _repository.GetRangeAsync(At(from), At(to), Ct);

    [Fact]
    public async Task Open_update_close_roundtrip()
    {
        var opened = Active(0, 0, open: true);
        var checkpoint = opened with { End = At(0.25) };
        var closed = checkpoint with { End = At(1), IsOpen = false, Title = "RE: Budget" };

        await _repository.ApplyAsync([new(IntervalChangeKind.Opened, opened)], Ct);
        await _repository.FlushAsync(Ct);
        (await RangeAsync(0, 10)).ShouldHaveSingleItem().ShouldBe(opened);

        await _repository.ApplyAsync([new(IntervalChangeKind.Updated, checkpoint)], Ct);
        await _repository.FlushAsync(Ct);
        (await RangeAsync(0, 10)).ShouldHaveSingleItem().ShouldBe(checkpoint);

        await _repository.ApplyAsync([new(IntervalChangeKind.Closed, closed)], Ct);
        await _repository.FlushAsync(Ct);
        (await RangeAsync(0, 10)).ShouldHaveSingleItem().ShouldBe(closed);
    }

    [Fact]
    public async Task Away_intervals_have_no_app()
    {
        var away = Away(0, 5);
        await StoreAsync(away);

        (await RangeAsync(0, 10)).ShouldHaveSingleItem().ShouldBe(away);
    }

    [Fact]
    public async Task Range_query_returns_intervals_overlapping_the_range()
    {
        var before = Active(0, 10);
        var startsBefore = Away(10, 70);
        var inside = Active(70, 80);
        var endsAfter = Active(80, 200);
        var after = Active(200, 210);
        await StoreAsync(before, startsBefore, inside, endsAfter, after);

        var result = await RangeAsync(60, 120);

        result.Select(i => i.Id).ShouldBe([startsBefore.Id, inside.Id, endsAfter.Id]);
    }

    [Fact]
    public async Task Range_boundaries_are_half_open()
    {
        var endsAtFrom = Active(0, 60);
        var startsAtTo = Active(120, 130);
        await StoreAsync(endsAtFrom, startsAtTo);

        (await RangeAsync(60, 120)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_open_interval_counts_as_running_even_if_its_last_checkpoint_is_before_the_range()
    {
        var running = Active(0, 30, open: true);
        await _repository.ApplyAsync([new(IntervalChangeKind.Opened, running)], Ct);
        await _repository.FlushAsync(Ct);

        (await RangeAsync(60, 120)).ShouldHaveSingleItem().Id.ShouldBe(running.Id);
    }

    [Fact]
    public async Task Apps_are_stored_once_and_keep_the_latest_display_name()
    {
        await StoreAsync(Active(0, 1), Active(1, 2), Active(2, 3, app: OutlookApp with { DisplayName = "Mail" }));

        using var connection = _database.Connect();
        connection.Query<(string, string)>("SELECT key, display_name FROM apps;").ShouldBe([("outlook", "Mail")]);
        (await RangeAsync(0, 3)).ShouldAllBe(i => i.App!.Key == "outlook");
    }

    [Fact]
    public async Task Apply_does_not_wait_for_the_disk_and_dispose_writes_what_is_queued()
    {
        var interval = Active(0, 1);
        await _repository.ApplyAsync([new(IntervalChangeKind.Closed, interval)], Ct);
        await _repository.DisposeAsync();

        await using var reopened = new IntervalRepository(_database);
        (await reopened.GetRangeAsync(At(0), At(1), Ct)).ShouldHaveSingleItem().ShouldBe(interval);
        _repository = reopened;
    }

    [Fact]
    public async Task A_failed_write_is_reported_to_flush_and_the_writer_keeps_going()
    {
        var invalid = Active(5, 1); // end before start violates a CHECK constraint
        await _repository.ApplyAsync([new(IntervalChangeKind.Closed, invalid)], Ct);
        await Should.ThrowAsync<Microsoft.Data.Sqlite.SqliteException>(() => _repository.FlushAsync(Ct).AsTask());

        var valid = Active(0, 1);
        await StoreAsync(valid);

        (await RangeAsync(0, 10)).ShouldHaveSingleItem().ShouldBe(valid);
    }

    [Fact]
    public async Task Apply_after_dispose_throws()
    {
        await _repository.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() =>
            _repository.ApplyAsync([new(IntervalChangeKind.Closed, Active(0, 1))], Ct).AsTask());

        _repository = new IntervalRepository(_database);
    }

    [Fact]
    public async Task Stores_exactly_what_the_engine_produces()
    {
        var engine = new TrackingEngine(new AliasAppIdentityResolver(AppAliasTable.BuiltIn));
        var expected = new IntervalTimeline();

        async Task Send(TrackingEvent e)
        {
            var changes = engine.Handle(e);
            expected.Apply(changes);
            await _repository.ApplyAsync(changes, Ct);
        }

        await Send(new TrackingStarted(At(0), Outlook()));
        for (var s = 1; s <= 90; s++)
        {
            if (s == 30)
            {
                await Send(new ForegroundChanged(At(s / 60.0), Code()));
            }

            await Send(new Tick(At(s / 60.0), At(s / 60.0)));
        }

        await Send(new TrackingStopped(At(1.5)));
        await _repository.FlushAsync(Ct);

        var stored = await RangeAsync(0, 10);
        stored.ShouldBe(expected.Intervals);
        stored.Select(i => i.App!.Key).ShouldBe(["outlook", "vscode"]);
    }
}
