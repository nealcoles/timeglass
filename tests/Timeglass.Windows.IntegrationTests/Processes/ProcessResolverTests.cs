using Timeglass.Windows.Processes;

namespace Timeglass.Windows.IntegrationTests.Processes;

public class ProcessResolverTests
{
    private const string OutlookPath = @"C:\Program Files\WindowsApps\Microsoft.OutlookForWindows\olk.exe";

    private readonly FakeOpener _opener = new();
    private readonly Dictionary<string, string?> _descriptions = new(StringComparer.OrdinalIgnoreCase);
    private int _descriptionReads;

    private ProcessResolver CreateResolver(int capacity = ProcessResolver.DefaultCapacity) =>
        new(_opener, path =>
        {
            _descriptionReads++;
            return _descriptions.GetValueOrDefault(path);
        }, capacity);

    [Fact]
    public void Resolves_path_name_and_description()
    {
        _opener.Add(10, startTime: 1, OutlookPath);
        _descriptions[OutlookPath] = "Outlook";

        var info = CreateResolver().Resolve(10);

        info.ShouldBe(new ProcessInfo(OutlookPath, "olk.exe", "Outlook"));
    }

    [Fact]
    public void Access_denied_resolves_to_the_elevated_placeholder()
    {
        _opener.Deny(10);

        var info = CreateResolver().Resolve(10);

        info.ShouldBe(ProcessInfo.Elevated);
        info.ExeName.ShouldBe("unknown-elevated");
        info.ExePath.ShouldBeEmpty();
        info.FileDescription.ShouldBe("Elevated app");
    }

    [Fact]
    public void Exited_process_resolves_to_unknown()
    {
        CreateResolver().Resolve(10).ShouldBe(ProcessInfo.Unknown);
    }

    [Fact]
    public void Process_id_zero_is_unknown_without_opening_anything()
    {
        CreateResolver().Resolve(0).ShouldBe(ProcessInfo.Unknown);

        _opener.Opens.ShouldBe(0);
    }

    [Fact]
    public void Unreadable_image_path_resolves_to_unknown()
    {
        _opener.Add(10, startTime: 1, path: null);

        CreateResolver().Resolve(10).ShouldBe(ProcessInfo.Unknown);
    }

    [Fact]
    public void Same_process_is_looked_up_once()
    {
        _opener.Add(10, startTime: 1, OutlookPath);
        var resolver = CreateResolver();

        resolver.Resolve(10);
        resolver.Resolve(10);
        resolver.Resolve(10);

        _opener.PathReads.ShouldBe(1);
        _opener.OpenHandles.ShouldBe(0);
    }

    [Fact]
    public void Reused_process_id_is_looked_up_again()
    {
        var resolver = CreateResolver();
        _opener.Add(10, startTime: 1, OutlookPath);
        resolver.Resolve(10);

        _opener.Add(10, startTime: 2, @"C:\Windows\notepad.exe");
        var info = resolver.Resolve(10);

        info.ExeName.ShouldBe("notepad.exe");
        _opener.PathReads.ShouldBe(2);
    }

    [Fact]
    public void Description_is_read_once_per_exe_path()
    {
        _opener.Add(10, startTime: 1, OutlookPath);
        _opener.Add(11, startTime: 1, OutlookPath.ToUpperInvariant());
        var resolver = CreateResolver();

        resolver.Resolve(10);
        resolver.Resolve(11);

        _descriptionReads.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_description_is_null(string? description)
    {
        _opener.Add(10, startTime: 1, OutlookPath);
        _descriptions[OutlookPath] = description;

        CreateResolver().Resolve(10).FileDescription.ShouldBeNull();
    }

    [Fact]
    public void Failures_are_not_cached()
    {
        var resolver = CreateResolver();
        _opener.Deny(10);
        resolver.Resolve(10);

        _opener.Add(10, startTime: 1, OutlookPath);

        resolver.Resolve(10).ExeName.ShouldBe("olk.exe");
    }

    [Fact]
    public void Old_processes_are_evicted_when_the_cache_is_full()
    {
        var resolver = CreateResolver(capacity: 2);
        _opener.Add(1, startTime: 1, @"C:\a.exe");
        _opener.Add(2, startTime: 1, @"C:\b.exe");
        _opener.Add(3, startTime: 1, @"C:\c.exe");

        resolver.Resolve(1);
        resolver.Resolve(2);
        resolver.Resolve(3);
        resolver.Resolve(1);

        _opener.PathReads.ShouldBe(4);
    }

    private sealed class FakeOpener : IProcessOpener
    {
        private readonly Dictionary<uint, (long StartTime, string? Path)?> _processes = [];

        public int Opens { get; private set; }

        public int PathReads { get; private set; }

        public int OpenHandles { get; private set; }

        public void Add(uint processId, long startTime, string? path) => _processes[processId] = (startTime, path);

        public void Deny(uint processId) => _processes[processId] = null;

        public ProcessOpenResult Open(uint processId)
        {
            Opens++;
            if (!_processes.TryGetValue(processId, out var process))
            {
                return ProcessOpenResult.NotFound;
            }

            if (process is not { } p)
            {
                return ProcessOpenResult.AccessDenied;
            }

            OpenHandles++;
            return ProcessOpenResult.Opened(new FakeProcess(this, p.StartTime, p.Path));
        }

        private sealed class FakeProcess(FakeOpener opener, long startTime, string? path) : IOpenProcess
        {
            public long StartTime => startTime;

            public string? ReadImagePath()
            {
                opener.PathReads++;
                return path;
            }

            public void Dispose() => opener.OpenHandles--;
        }
    }
}
