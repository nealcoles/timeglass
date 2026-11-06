using Timeglass.Api;

namespace Timeglass.App.Tests;

/// <summary>The dashboard embedded by <c>Dashboard.targets</c> (#30).</summary>
public sealed class DashboardFilesTests
{
    [Fact]
    public void The_release_build_embeds_the_built_dashboard()
    {
#if DEBUG
        Assert.Skip("Debug builds embed web/dist only if it was built; Release builds always do.");
#endif
        var files = DashboardFiles.Load().ShouldNotBeNull();

        var index = files.GetFileInfo(TimeglassDashboard.IndexFile);
        using (var reader = new StreamReader(index.CreateReadStream()))
        {
            var html = reader.ReadToEnd();
            html.ShouldContain("<div id=\"root\">");
            html.ShouldContain("/assets/");
        }

        files.GetDirectoryContents("assets").ShouldContain(f => f.Name.EndsWith(".js", StringComparison.Ordinal));
    }
}
