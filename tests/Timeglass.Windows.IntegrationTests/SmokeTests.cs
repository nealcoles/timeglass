namespace Timeglass.Windows.IntegrationTests;

[Trait("Category", "WindowsIntegration")]
public class SmokeTests
{
    [Fact]
    public void Runs_on_windows()
    {
        OperatingSystem.IsWindows().ShouldBeTrue();
        typeof(WindowsAssembly).Assembly.GetName().Name.ShouldBe("Timeglass.Windows");
    }
}
