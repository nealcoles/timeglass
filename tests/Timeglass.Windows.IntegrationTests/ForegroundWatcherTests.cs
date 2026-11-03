using System.Collections.Concurrent;
using System.Diagnostics;
using Timeglass.Core.Domain;
using Timeglass.Windows.Foreground;

namespace Timeglass.Windows.IntegrationTests;

[Trait("Category", Desktop.Category)]
[Collection(ForegroundCollection.Name)]
public sealed class ForegroundWatcherTests : IDisposable
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private readonly TestDesktop _desktop = new();
    private readonly ConcurrentQueue<WindowSnapshot?> _events = new();

    public void Dispose() => _desktop.Dispose();

    private Win32ForegroundWindowSource Watch(ForegroundWatcherOptions? options = null)
    {
        var source = new Win32ForegroundWindowSource(options);
        source.ForegroundChanged += _events.Enqueue;
        return source;
    }

    private static string UniqueTitle(string name) => $"Timeglass test {name} {Guid.NewGuid():N}";

    /// <summary>Waits up to <paramref name="timeout"/> for an event with this title; null if none came.</summary>
    private async Task<WindowSnapshot?> FindTitleAsync(string title, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var match = _events.FirstOrDefault(e => e?.Title == title);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        return null;
    }

    private async Task<WindowSnapshot> WaitForTitleAsync(string title) =>
        await FindTitleAsync(title, EventTimeout)
        ?? throw new ShouldAssertException(
            $"No foreground event with title '{title}'. Seen: {string.Join(", ", _events.Select(e => e?.Title ?? "<none>"))}");

    [Fact(Timeout = Desktop.TestTimeout)]
    public async Task Detects_a_foreground_change()
    {
        var first = _desktop.Open(UniqueTitle("first"));
        var second = _desktop.Open(UniqueTitle("second"));
        _desktop.Activate(first);
        using var watcher = Watch();

        _desktop.Activate(second);

        var snapshot = await WaitForTitleAsync(second.Text);
        snapshot.ProcessId.ShouldBe(Environment.ProcessId);
        snapshot.ExePath.ShouldBe(Environment.ProcessPath, StringCompareShould.IgnoreCase);
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public async Task Detects_a_title_change_of_the_foreground_window()
    {
        var window = _desktop.Open(UniqueTitle("before"));
        _desktop.Activate(window);
        using var watcher = Watch(new ForegroundWatcherOptions { PollInterval = TimeSpan.FromMinutes(10) });

        var renamed = UniqueTitle("after");
        _desktop.SetTitle(window, renamed);

        await WaitForTitleAsync(renamed);
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public async Task Title_changes_of_background_windows_are_ignored()
    {
        var front = _desktop.Open(UniqueTitle("front"));
        var back = _desktop.Open(UniqueTitle("back"));
        _desktop.Activate(front);
        using var watcher = Watch(new ForegroundWatcherOptions { PollInterval = TimeSpan.FromMinutes(10) });

        var renamed = UniqueTitle("back renamed");
        _desktop.SetTitle(back, renamed);

        (await FindTitleAsync(renamed, TimeSpan.FromSeconds(1))).ShouldBeNull();
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public async Task Polling_catches_a_change_when_hooks_are_off()
    {
        var first = _desktop.Open(UniqueTitle("first"));
        var second = _desktop.Open(UniqueTitle("second"));
        _desktop.Activate(first);
        using var watcher = Watch(new ForegroundWatcherOptions { UseHooks = false, PollInterval = TimeSpan.FromMilliseconds(200) });

        _desktop.Activate(second);

        await WaitForTitleAsync(second.Text);
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public void GetCurrent_reads_the_foreground_window_of_this_process()
    {
        var window = _desktop.Open(UniqueTitle("current"));
        _desktop.Activate(window);
        using var watcher = Watch();

        var snapshot = watcher.GetCurrent().ShouldNotBeNull();

        snapshot.Title.ShouldBe(window.Text);
        snapshot.ProcessId.ShouldBe(Environment.ProcessId);
        snapshot.ExePath.ShouldBe(Environment.ProcessPath, StringCompareShould.IgnoreCase);
        snapshot.ExeName.ShouldBe(Path.GetFileName(Environment.ProcessPath), StringCompareShould.IgnoreCase);
        var description = FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileDescription;
        snapshot.FileDescription.ShouldBe(string.IsNullOrWhiteSpace(description) ? null : description.Trim());
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public async Task No_events_after_dispose()
    {
        var window = _desktop.Open(UniqueTitle("disposed"));
        _desktop.Activate(window);
        var watcher = Watch();

        watcher.Dispose();
        watcher.Dispose(); // twice is fine

        var renamed = UniqueTitle("after dispose");
        _desktop.SetTitle(window, renamed);
        (await FindTitleAsync(renamed, TimeSpan.FromSeconds(1))).ShouldBeNull();
    }
}
