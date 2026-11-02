using System.Reflection;
using Timeglass.Api;
using Timeglass.Storage;

namespace Timeglass.Core.Tests;

/// <summary>
/// Enforces the dependency rule from docs/architecture.md:
/// Core depends on nothing of ours, and the portable layers never depend on Windows-only code.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] WindowsOnlyAssemblies =
    [
        "Timeglass.Windows",
        "Timeglass",
        "System.Windows.Forms",
        "Microsoft.Win32.SystemEvents",
    ];

    [Fact]
    public void Core_does_not_reference_other_timeglass_assemblies()
    {
        var references = ReferencedNames(typeof(CoreAssembly).Assembly);

        references.ShouldNotContain(name => name.StartsWith("Timeglass", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(typeof(CoreAssembly))]
    [InlineData(typeof(StorageAssembly))]
    [InlineData(typeof(ApiAssembly))]
    public void Portable_assemblies_do_not_reference_windows_only_assemblies(Type marker)
    {
        var references = ReferencedNames(marker.Assembly);

        references.Intersect(WindowsOnlyAssemblies).ShouldBeEmpty();
    }

    private static string[] ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();
}
