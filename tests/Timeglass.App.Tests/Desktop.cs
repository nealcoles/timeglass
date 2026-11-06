namespace Timeglass.App.Tests;

/// <summary>Settings for the tests that use real Windows APIs; see Timeglass.Windows.IntegrationTests.</summary>
public static class Desktop
{
    /// <summary>Same trait as the Windows integration tests, so CI runs these in that step.</summary>
    public const string Category = "WindowsIntegration";

    /// <summary>Per-test timeout, so a hung message loop fails one test instead of stalling CI.</summary>
    public const int TestTimeout = 30_000;

    /// <summary>Runs <paramref name="action"/> on a new STA thread, like the app's UI thread.</summary>
    public static Task RunOnStaThreadAsync(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                done.SetResult();
            }
#pragma warning disable CA1031 // Handed to the test.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                done.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Test UI thread",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}

/// <summary>A temp folder for a database and settings file, deleted on dispose.</summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "timeglass-tests", Guid.NewGuid().ToString("N"));

    public string DatabasePath => System.IO.Path.Combine(Path, "timeglass.db");

    public string SettingsPath => System.IO.Path.Combine(Path, "settings.json");

    public string ApiRuntimeFilePath => System.IO.Path.Combine(Path, "api.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
