using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Timeglass.Core.Domain;
using Timeglass.Storage;
using Timeglass.Testing;

namespace Timeglass.App.Tests;

public sealed class DatabaseUpkeepTests : IAsyncLifetime
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
    private static readonly AppIdentity Code = new("code", "Code");

    private readonly TempFolder _folder = new();
    private readonly FakeTimeProvider _time = new(Utc("2026-11-16T12:00Z"));
    private TimeglassDatabase _database = null!;
    private IntervalRepository _repository = null!;
    private int? _retentionMonths;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _time.SetLocalTimeZone(Oslo);
        _database = TimeglassDatabase.Open(_folder.DatabasePath);
        _repository = new IntervalRepository(_database);
        await _repository.ApplyAsync(
        [
            Closed("2026-01-10T09:00Z", "2026-01-10T10:00Z"),
            Closed("2026-11-10T09:00Z", "2026-11-10T10:00Z"),
        ], Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _repository.DisposeAsync();
        _database.Dispose();
        _folder.Dispose();
    }

    private static DateTimeOffset Utc(string at) => DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static IntervalChange Closed(string start, string end) => new(
        IntervalChangeKind.Closed,
        new ActivityInterval(Guid.CreateVersion7(), IntervalKind.Active, Utc(start), Utc(end), false, Code, null, "title"));

    private DatabaseUpkeep CreateUpkeep() => new(
        _repository, new FakeIdleSource(_time), () => _retentionMonths, _time, NullLogger<DatabaseUpkeep>.Instance);

    private Task<IReadOnlyList<ActivityInterval>> StoredAsync() =>
        _repository.GetRangeAsync(Utc("2025-01-01T00:00Z"), Utc("2027-01-01T00:00Z"), Ct);

    [Theory]
    [InlineData("2026-11-16T12:00Z", 1, "2026-10-15T22:00Z")] // midnight 16 October, summer time
    [InlineData("2026-11-16T12:00Z", 12, "2025-11-15T23:00Z")]
    [InlineData("2026-03-31T21:30Z", 1, "2026-02-27T23:00Z")] // 31 March has no 31 February: the 28th
    public void Retention_keeps_whole_local_days(string now, int months, string expected) =>
        DatabaseUpkeep.RetentionCutoff(Utc(now), Oslo, months).ShouldBe(Utc(expected));

    [Fact]
    public async Task Without_a_retention_setting_nothing_is_rolled_up()
    {
        using var upkeep = CreateUpkeep();

        await upkeep.RunOnceAsync(Ct);

        (await StoredAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task With_a_retention_setting_old_days_become_totals()
    {
        _retentionMonths = 6;
        using var upkeep = CreateUpkeep();

        await upkeep.RunOnceAsync(Ct);

        (await StoredAsync()).ShouldHaveSingleItem().Start.ShouldBe(Utc("2026-11-10T09:00Z"));
        var january = await _repository.GetDaySummaryAsync(new DateOnly(2026, 1, 10), Oslo, _time.GetUtcNow(), cancellationToken: Ct);
        january.Active.ShouldBe(TimeSpan.FromHours(1));
    }
}
