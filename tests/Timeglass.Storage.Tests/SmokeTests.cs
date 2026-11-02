namespace Timeglass.Storage.Tests;

public class SmokeTests
{
    [Fact]
    public void Storage_assembly_loads()
    {
        typeof(StorageAssembly).Assembly.GetName().Name.ShouldBe("Timeglass.Storage");
    }
}
