using Timeglass.Storage.Migrations;

namespace Timeglass.Storage.Tests;

/// <summary>A database file in its own temp folder, deleted on dispose.</summary>
public sealed class TempDatabase : IDisposable
{
    private readonly string _folder =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "timeglass-tests", Guid.NewGuid().ToString("N"));

    public string Path => System.IO.Path.Combine(_folder, "timeglass.db");

    /// <summary>Opens the file. Callers dispose the returned database before this one.</summary>
    public TimeglassDatabase Open(IReadOnlyList<Migration>? migrations = null) =>
        TimeglassDatabase.Open(Path, migrations);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
