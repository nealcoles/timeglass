namespace Timeglass.Windows.IntegrationTests;

/// <summary>Settings shared by the tests that touch the real Windows desktop and APIs.</summary>
public static class Desktop
{
    /// <summary>
    /// Trait value for tests that call real Win32 APIs. CI runs them in their own step:
    /// <c>dotnet test --filter Category=WindowsIntegration</c>.
    /// </summary>
    public const string Category = "WindowsIntegration";

    /// <summary>Per-test timeout, so a hung message loop fails one test instead of stalling CI.</summary>
    public const int TestTimeout = 30_000;
}

/// <summary>
/// Tests that change which window is in front. They run one at a time, after the parallel
/// tests, so they don't steal the foreground from each other.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ForegroundCollection
{
    public const string Name = "Foreground";
}
