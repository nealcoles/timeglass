using Timeglass.Core.Domain;
using Timeglass.Core.Ports;

namespace Timeglass.Testing;

/// <summary>Foreground source driven by the test.</summary>
public sealed class FakeForegroundWindowSource : IForegroundWindowSource
{
    public event Action<WindowSnapshot?>? ForegroundChanged;

    public WindowSnapshot? Current { get; set; }

    public WindowSnapshot? GetCurrent() => Current;

    /// <summary>Makes <paramref name="window"/> the foreground window and raises the event.</summary>
    public void SwitchTo(WindowSnapshot? window)
    {
        Current = window;
        ForegroundChanged?.Invoke(window);
    }

    public bool HasSubscribers => ForegroundChanged is not null;
}

/// <summary>Idle source driven by the test. Defaults to "input just happened".</summary>
public sealed class FakeIdleSource(TimeProvider time) : IIdleSource
{
    private DateTimeOffset? _lastInput;

    public DateTimeOffset GetLastInputTime() => _lastInput ?? time.GetUtcNow();

    /// <summary>Freezes the last input time (the user walked away).</summary>
    public void SetLastInput(DateTimeOffset at) => _lastInput = at;

    /// <summary>Back to "input just happened".</summary>
    public void Active() => _lastInput = null;
}

/// <summary>Session event source driven by the test.</summary>
public sealed class FakeSessionEventSource : ISessionEventSource
{
    public event Action<SessionEvent>? SessionChanged;

    public void Raise(SessionEvent sessionEvent) => SessionChanged?.Invoke(sessionEvent);
}

/// <summary>Sink that records every change and keeps the latest state of each interval.</summary>
public sealed class RecordingIntervalSink : IIntervalSink
{
    private readonly Lock _gate = new();
    private readonly List<IntervalChange> _changes = [];
    private readonly IntervalTimeline _timeline = new();

    /// <summary>When set, <see cref="ApplyAsync"/> throws this instead of recording.</summary>
    public Exception? FailWith { get; set; }

    public IReadOnlyList<IntervalChange> Changes
    {
        get
        {
            lock (_gate)
            {
                return [.. _changes];
            }
        }
    }

    public IReadOnlyList<ActivityInterval> Intervals
    {
        get
        {
            lock (_gate)
            {
                return _timeline.Intervals;
            }
        }
    }

    public ValueTask ApplyAsync(IReadOnlyList<IntervalChange> changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (FailWith is not null)
        {
            throw FailWith;
        }

        lock (_gate)
        {
            _changes.AddRange(changes);
            _timeline.Apply(changes);
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>Applies changes as upserts by id, the same way storage does.</summary>
public sealed class IntervalTimeline
{
    private readonly Dictionary<Guid, ActivityInterval> _byId = [];

    public IReadOnlyList<ActivityInterval> Intervals => [.. _byId.Values.OrderBy(i => i.Start).ThenBy(i => i.End)];

    public void Apply(IEnumerable<IntervalChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var change in changes)
        {
            _byId[change.Interval.Id] = change.Interval;
        }
    }
}

/// <summary>Ready-made windows for tests.</summary>
public static class TestWindows
{
    public static WindowSnapshot Outlook(string? title = "Inbox") =>
        new(100, @"C:\Program Files\WindowsApps\Microsoft.OutlookForWindows\olk.exe", "olk.exe", title, "Outlook");

    public static WindowSnapshot Code(string? title = "Program.cs - timeglass") =>
        new(200, @"C:\Program Files\Microsoft VS Code\Code.exe", "Code.exe", title, "Visual Studio Code");

    public static WindowSnapshot Teams(string? title = "Chat") =>
        new(300, @"C:\Program Files\WindowsApps\MSTeams\ms-teams.exe", "ms-teams.exe", title, "Microsoft Teams");

    public static WindowSnapshot Browser(string? title = "GitHub") =>
        new(400, @"C:\Program Files\Mozilla Firefox\firefox.exe", "firefox.exe", title, "Firefox");
}
