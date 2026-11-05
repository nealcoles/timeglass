using Microsoft.Win32;
using Timeglass.Windows.Autostart;

namespace Timeglass.Windows.IntegrationTests.Autostart;

/// <summary>Writes to the real startup keys under a test-only value name, and always cleans up.</summary>
[Trait("Category", Desktop.Category)]
public sealed class CurrentUserStartupRegistryTests : IDisposable
{
    private readonly CurrentUserStartupRegistry _registry = CurrentUserStartupRegistry.Instance;
    private readonly string _name = $"Timeglass.IntegrationTest.{Guid.NewGuid():N}";

    public void Dispose()
    {
        _registry.DeleteRunValue(_name);
        _registry.DeleteApprovedValue(_name);
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public void Writes_reads_and_removes_a_run_value()
    {
        _registry.GetRunValue(_name).ShouldBeNull();

        _registry.SetRunValue(_name, "\"C:\\Timeglass test\\Timeglass.exe\"");

        _registry.GetRunValue(_name).ShouldBe("\"C:\\Timeglass test\\Timeglass.exe\"");
        ReadDirectly(CurrentUserStartupRegistry.RunKey).ShouldBe("\"C:\\Timeglass test\\Timeglass.exe\"");

        _registry.DeleteRunValue(_name);

        _registry.GetRunValue(_name).ShouldBeNull();
        ReadDirectly(CurrentUserStartupRegistry.RunKey).ShouldBeNull();
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public void Deleting_missing_values_does_nothing()
    {
        Should.NotThrow(() =>
        {
            _registry.DeleteRunValue(_name);
            _registry.DeleteApprovedValue(_name);
        });
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public void Reads_and_removes_a_Task_Manager_flag()
    {
        byte[] off = [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        using (var key = Registry.CurrentUser.CreateSubKey(CurrentUserStartupRegistry.ApprovedKey, writable: true))
        {
            key.SetValue(_name, off, RegistryValueKind.Binary);
        }

        _registry.GetApprovedValue(_name).ShouldBe(off);

        _registry.DeleteApprovedValue(_name);

        _registry.GetApprovedValue(_name).ShouldBeNull();
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public void Registration_round_trips_through_the_registry()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Timeglass.exe");
        var autostart = new AutostartRegistration(_registry, exe, _name);

        autostart.Enable();
        autostart.IsEnabled.ShouldBeTrue();
        ReadDirectly(CurrentUserStartupRegistry.RunKey).ShouldBe($"\"{exe}\"");

        autostart.Disable();
        autostart.IsEnabled.ShouldBeFalse();
        ReadDirectly(CurrentUserStartupRegistry.RunKey).ShouldBeNull();
    }

    private object? ReadDirectly(string subKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey);
        return key?.GetValue(_name);
    }
}
