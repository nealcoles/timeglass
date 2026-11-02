namespace Timeglass.Api.Tests;

public class SmokeTests
{
    [Fact]
    public void Api_assembly_loads()
    {
        typeof(ApiAssembly).Assembly.GetName().Name.ShouldBe("Timeglass.Api");
    }
}
