using Timeglass.Core.Domain;

namespace Timeglass.DevHost;

/// <summary>
/// Realistic, reproducible tracking data: a few workdays in Outlook, Teams, VS Code, Edge and
/// Windows Terminal, with a standup, a lunch break, short idle breaks, title changes a few
/// seconds apart, and a late evening on Tuesdays that runs past midnight. Today runs up to <c>now</c>,
/// with the last interval still open.
/// </summary>
/// <remarks>
/// <para>
/// The output depends only on the arguments. Each date gets its own random sequence from the
/// seed and the date, so a given day looks the same whichever day is today.
/// </para>
/// <para>
/// Used by the dev host, by the API tests and, through the dev host, by the end-to-end tests.
/// Change it with care: tests count on its shape (see <c>DemoDataTests</c>), not its exact numbers.
/// </para>
/// </remarks>
public static class DemoData
{
    /// <summary>The seed used unless another is given.</summary>
    public const int DefaultSeed = 2026;

    /// <summary>How many workdays before today get data unless another number is given.</summary>
    public const int DefaultWorkdays = 5;

    public static readonly AppIdentity Outlook = new("outlook", "Outlook");
    public static readonly AppIdentity Teams = new("teams", "Microsoft Teams");
    public static readonly AppIdentity Code = new("vscode", "Visual Studio Code");
    public static readonly AppIdentity Edge = new("edge", "Microsoft Edge");
    public static readonly AppIdentity Terminal = new("terminal", "Windows Terminal");

    private static readonly (AppIdentity App, string Exe, int Weight, string[] Titles)[] Apps =
    [
        (Code, @"C:\Users\demo\AppData\Local\Programs\Microsoft VS Code\Code.exe", 8,
        [
            "TrackingEngine.cs - timeglass - Visual Studio Code",
            "IntervalRepository.cs - timeglass - Visual Studio Code",
            "DaySummaries.cs - timeglass - Visual Studio Code",
            "App.tsx - timeglass - Visual Studio Code",
            "README.md - timeglass - Visual Studio Code",
        ]),
        (Outlook, @"C:\Program Files\WindowsApps\Microsoft.OutlookForWindows\olk.exe", 5,
        [
            "Inbox - demo@example.com - Outlook",
            "RE: Q4 budget - Outlook",
            "Calendar - Outlook",
            "Release notes draft - Message - Outlook",
        ]),
        (Edge, @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", 4,
        [
            "Pull requests · sevenam/timeglass - Work - Microsoft Edge",
            "TimeZoneInfo Class (System) | Microsoft Learn - Work - Microsoft Edge",
            "SQLite Query Language: Aggregate Functions - Work - Microsoft Edge",
            "Recharts - Work - Microsoft Edge",
        ]),
        (Teams, @"C:\Program Files\WindowsApps\MSTeams\ms-teams.exe", 3,
        [
            "Chat | Anna Berg | Microsoft Teams",
            "Platform team | General | Microsoft Teams",
        ]),
        (Terminal, @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal\WindowsTerminal.exe", 2,
        [
            "dotnet test",
            "PowerShell",
        ]),
    ];

    private static readonly string CodeExe = Apps[0].Exe;
    private static readonly string TeamsExe = Apps[3].Exe;

    /// <summary>
    /// The intervals of today and the <paramref name="workdays"/> weekdays before it, in
    /// <paramref name="zone"/>, ordered by start. Nothing reaches past <paramref name="now"/>; the
    /// interval running at <paramref name="now"/> is open, and today has nothing before 08:00.
    /// Weekends before today are empty.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="workdays"/> is negative.</exception>
    public static IReadOnlyList<ActivityInterval> Generate(
        DateTimeOffset now,
        TimeZoneInfo zone,
        int workdays = DefaultWorkdays,
        int seed = DefaultSeed)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentOutOfRangeException.ThrowIfNegative(workdays);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var days = new List<DateOnly>();
        for (var day = today.AddDays(-1); days.Count < workdays; day = day.AddDays(-1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(day);
            }
        }

        days.Reverse();
        days.Add(today);

        var intervals = new List<ActivityInterval>();
        foreach (var day in days)
        {
            // Tuesdays have a late evening that runs past midnight. With the default five
            // workdays the data always reaches back to a Tuesday before today.
            intervals.AddRange(new DayWriter(day, zone, seed, now).Write(lateEvening: day.DayOfWeek == DayOfWeek.Tuesday));
        }

        return intervals;
    }

    /// <summary>Writes one day's intervals, keeping a cursor so they never overlap.</summary>
    private sealed class DayWriter
    {
        private readonly DateOnly _day;
        private readonly TimeZoneInfo _zone;
        private readonly DateTimeOffset _now;
        private readonly Random _random;
        private readonly List<ActivityInterval> _intervals = [];
        private DateTimeOffset _cursor;

        public DayWriter(DateOnly day, TimeZoneInfo zone, int seed, DateTimeOffset now)
        {
            _day = day;
            _zone = zone;
            _now = now;
            // Not HashCode.Combine: that is randomised per process, and the data must not be.
            _random = new Random(unchecked((seed * 397) ^ day.DayNumber));
        }

        private bool Done => _cursor >= _now;

        public List<ActivityInterval> Write(bool lateEvening)
        {
            var isToday = _day == DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_now, _zone).DateTime);
            _cursor = Local(TimeSpan.FromHours(8) + Minutes(0, 30));
            var standup = Local(TimeSpan.FromHours(9.5));
            var lunch = Local(TimeSpan.FromHours(11.5) + Minutes(0, 20));
            var end = isToday ? _now : Local(TimeSpan.FromHours(16) + Minutes(0, 60));

            // The morning: work until the standup, the standup, work until lunch.
            WorkUntil(standup);
            Add(IntervalKind.Active, Teams, TeamsExe, "Daily standup | Microsoft Teams", Minutes(15, 20));
            WorkUntil(lunch);
            Add(IntervalKind.Away, null, null, null, Minutes(30, 45));

            // The afternoon, and for today everything up to now.
            WorkUntil(end);

            if (lateEvening && !Done)
            {
                // Back at the desk at 22:30 and at it until well past midnight.
                _cursor = Local(TimeSpan.FromHours(22.5));
                Add(IntervalKind.Active, Code, CodeExe, "Migrator.cs - timeglass - Visual Studio Code", TimeSpan.FromMinutes(95));
                Add(IntervalKind.Active, Code, CodeExe, "0002_single_open_interval.sql - timeglass - Visual Studio Code", TimeSpan.FromMinutes(35));
            }

            return _intervals;
        }

        /// <summary>Stretches in random apps, with a short idle break now and then, until <paramref name="until"/>.</summary>
        private void WorkUntil(DateTimeOffset until)
        {
            while (_cursor < until && !Done)
            {
                if (_random.Next(6) == 0)
                {
                    Add(IntervalKind.Away, null, null, null, Clamp(Minutes(5, 15), until));
                    continue;
                }

                var (app, exe, _, titles) = PickApp();
                var stretchEnd = Min(_cursor + Minutes(4, 40), until);
                while (_cursor < stretchEnd && !Done)
                {
                    // A new title every few minutes. Sometimes the switch leaves a gap of a few
                    // seconds (a dialog in front), which the timeline merges over.
                    Add(IntervalKind.Active, app, exe, titles[_random.Next(titles.Length)], Clamp(Minutes(1, 12), stretchEnd));
                    if (_random.Next(4) == 0)
                    {
                        _cursor += TimeSpan.FromSeconds(_random.Next(1, 4));
                    }
                }
            }
        }

        private (AppIdentity App, string Exe, int Weight, string[] Titles) PickApp()
        {
            var pick = _random.Next(Apps.Sum(a => a.Weight));
            foreach (var app in Apps)
            {
                pick -= app.Weight;
                if (pick < 0)
                {
                    return app;
                }
            }

            return Apps[^1];
        }

        /// <summary>Adds an interval at the cursor and moves the cursor to its end. Stops at now, leaving it open.</summary>
        private void Add(IntervalKind kind, AppIdentity? app, string? exe, string? title, TimeSpan length)
        {
            if (Done || length <= TimeSpan.Zero)
            {
                return;
            }

            var end = _cursor + length;
            var open = end >= _now;
            _intervals.Add(new ActivityInterval(NewId(), kind, _cursor, open ? _now : end, open, app, exe, title));
            _cursor = open ? _now : end;
        }

        private TimeSpan Clamp(TimeSpan length, DateTimeOffset until) => Min(_cursor + length, until) - _cursor;

        /// <summary>A whole number of seconds between <paramref name="min"/> and <paramref name="max"/> minutes.</summary>
        private TimeSpan Minutes(int min, int max) => TimeSpan.FromSeconds(_random.Next(min * 60, (max * 60) + 1));

        private DateTimeOffset Local(TimeSpan timeOfDay)
        {
            var local = _day.ToDateTime(TimeOnly.MinValue) + timeOfDay;
            return new DateTimeOffset(local, _zone.GetUtcOffset(local)).ToUniversalTime();
        }

        private Guid NewId()
        {
            Span<byte> bytes = stackalloc byte[16];
            _random.NextBytes(bytes);
            return new Guid(bytes);
        }

        private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    }
}
