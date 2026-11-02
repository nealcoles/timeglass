namespace Timeglass.App;

internal static class Program
{
    /// <summary>
    /// Entry point. The tray host is implemented in #20; for now the app only initializes WinForms.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
    }
}
