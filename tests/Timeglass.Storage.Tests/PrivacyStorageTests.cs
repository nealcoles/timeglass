using Dapper;
using Microsoft.Extensions.Time.Testing;
using Timeglass.Core.Apps;
using Timeglass.Core.Domain;
using Timeglass.Core.Tracking;
using Timeglass.Testing;
using static Timeglass.Testing.TestWindows;

namespace Timeglass.Storage.Tests;

public sealed class PrivacyStorageTests : IAsyncLifetime
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

    private static ActivityInterval Active(double start, double end, bool open = false, string title = "RE: Salary review") =>
        new(Guid.CreateVersion7(), IntervalKind.Active, At(start), At(end), open, OutlookApp, @"C:\olk.exe", title);

    private async Task StoreAsync(params ActivityInterval[] intervals)
    {
        await _repository.ApplyAsync(
            [.. intervals.Select(i => new IntervalChange(i.IsOpen ? IntervalChangeKind.Opened : IntervalChangeKind.Closed, i))], Ct);
        await _repository.FlushAsync(Ct);
    }

    private async Task<IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)>> AllAsync() =>
        [.. (await _repository.GetRangeAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, Ct)).Select(i => (i.Start, i.End))];

    private string[] StoredTitles()
    {
        using var connection = _database.Connect();
        return [.. connection.Query<string?>("SELECT window_title FROM intervals").Where(t => t is not null).Select(t => t!)];
    }

    [Fact]
    public async Task Delete_range_removes_and_cuts_overlapping_intervals()
    {
        await StoreAsync(
            Active(0, 10),   // cut at the range start
            Active(10, 20),  // inside: removed
            Active(20, 20),  // empty, inside: removed
            Active(20, 40),  // cut at the range end
            Active(40, 50)); // after: untouched

        await _repository.DeleteRangeAsync(At(5), At(30), Ct);

        (await AllAsync()).ShouldBe([(At(0), At(5)), (At(30), At(40)), (At(40), At(50))]);
    }

    [Fact]
    public async Task Delete_range_inside_one_interval_splits_it_into_two_rows()
    {
        var interval = Active(0, 60);
        await StoreAsync(interval);

        await _repository.DeleteRangeAsync(At(10), At(20), Ct);

        var pieces = await _repository.GetRangeAsync(At(0), At(60), Ct);
        pieces.Select(p => (p.Start, p.End)).ShouldBe([(At(0), At(10)), (At(20), At(60))]);
        pieces[0].Id.ShouldBe(interval.Id);
        pieces[1].Id.ShouldNotBe(interval.Id);
        pieces.ShouldAllBe(p => p.App == OutlookApp && p.Title == interval.Title && p.ExePath == interval.ExePath && !p.IsOpen);
    }

    [Fact]
    public async Task Delete_range_leaves_the_open_interval_to_the_tracker()
    {
        var open = Active(0, 5, open: true);
        await StoreAsync(open);

        await _repository.DeleteRangeAsync(At(0), At(10), Ct);

        (await AllAsync()).ShouldBe([(At(0), At(5))]);
    }

    [Fact]
    public async Task Delete_range_runs_after_the_changes_queued_before_it()
    {
        await _repository.ApplyAsync([new(IntervalChangeKind.Closed, Active(0, 10))], Ct);

        await _repository.DeleteRangeAsync(At(0), At(10), Ct);

        (await AllAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleted_titles_are_not_left_in_the_file()
    {
        const string secret = "RE: Secret merger plans 7f3c";
        await StoreAsync(Active(0, 10, title: secret), Active(10, 20, title: "Inbox"));

        await _repository.DeleteRangeAsync(At(0), At(10), Ct);
        await _repository.DisposeAsync();
        _database.Dispose(); // checkpoints the WAL into the file and releases it
        _repository = new IntervalRepository(_database = _temp.Open());

        File.ReadAllText(_temp.Path, System.Text.Encoding.Latin1).ShouldNotContain(secret);
        StoredTitles().ShouldBe(["Inbox"]);
    }

    [Fact]
    public async Task A_range_that_ends_before_it_starts_is_rejected()
    {
        await Should.ThrowAsync<ArgumentException>(() => _repository.DeleteRangeAsync(At(5), At(4), Ct).AsTask());
    }

    [Fact]
    public async Task Excluded_apps_and_apps_without_titles_never_have_a_title_in_storage()
    {
        var time = new FakeTimeProvider(T0);
        var foreground = new FakeForegroundWindowSource { Current = Code() };
        var options = TrackingOptions.Default.WithExcludedApps("olk").WithAppsWithoutTitles("ms-teams");
        var service = new TrackingService(
            new TrackingEngine(new AliasAppIdentityResolver(AppAliasTable.Empty), options),
            foreground,
            new FakeIdleSource(time),
            new FakeSessionEventSource(),
            _repository,
            time);

        using var cts = new CancellationTokenSource();
        var run = service.RunAsync(cts.Token);
        await AdvanceAsync(time, 20);
        foreground.SwitchTo(Outlook("RE: Salary review"));
        await AdvanceAsync(time, 20);
        foreground.SwitchTo(Outlook("RE: Layoffs"));
        await AdvanceAsync(time, 20);
        foreground.SwitchTo(Teams("Chat with HR"));
        await AdvanceAsync(time, 20);
        await cts.CancelAsync();
        await run;

        var stored = await _repository.GetRangeAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, Ct);
        stored.Select(i => i.App?.Key).ShouldBe(["code", "ms-teams"]);
        StoredTitles().ShouldBe(["Program.cs - timeglass"]);
    }

    private static async Task AdvanceAsync(FakeTimeProvider time, int seconds)
    {
        for (var i = 0; i < seconds; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(1, Ct);
        }

        await Task.Delay(50, Ct);
    }
}
