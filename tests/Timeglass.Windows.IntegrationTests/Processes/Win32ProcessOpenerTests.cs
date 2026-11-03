using System.Diagnostics;
using Timeglass.Windows.Processes;

namespace Timeglass.Windows.IntegrationTests.Processes;

[Trait("Category", Desktop.Category)]
public class Win32ProcessOpenerTests
{
    [Fact(Timeout = Desktop.TestTimeout)]
    public void Opens_its_own_process()
    {
        var opened = Win32ProcessOpener.Instance.Open((uint)Environment.ProcessId);

        opened.Status.ShouldBe(ProcessOpenStatus.Opened);
        using var process = opened.Process.ShouldNotBeNull();
        process.ReadImagePath().ShouldBe(Environment.ProcessPath, StringCompareShould.IgnoreCase);
        using var self = Process.GetCurrentProcess();
        process.StartTime.ShouldBe(self.StartTime.ToFileTimeUtc());
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public void A_process_that_does_not_exist_is_not_found()
    {
        // Process ids are multiples of 4 and never get this high.
        Win32ProcessOpener.Instance.Open(0xFFFF_FFF0).Status.ShouldBe(ProcessOpenStatus.NotFound);
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public void Reads_the_file_description_of_its_own_exe()
    {
        var expected = FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileDescription;

        Win32ProcessOpener.ReadFileDescription(Environment.ProcessPath!).ShouldBe(expected);
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public void A_missing_exe_has_no_file_description()
    {
        Win32ProcessOpener.ReadFileDescription(@"C:\does\not\exist.exe").ShouldBeNull();
    }
}
