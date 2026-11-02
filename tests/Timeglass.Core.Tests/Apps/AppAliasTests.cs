using System.Text.Json;
using Timeglass.Core.Apps;
using Timeglass.Core.Domain;
using static Timeglass.Testing.TestWindows;

namespace Timeglass.Core.Tests.Apps;

public class AppAliasTests
{
    private static WindowSnapshot Window(string exeName, string? title = null, string? description = null) =>
        new(1, $@"C:\Apps\{exeName}", exeName, title, description);

    private static AppIdentity Resolve(AppAliasTable table, WindowSnapshot window) =>
        new AliasAppIdentityResolver(table).Resolve(window);

    [Fact]
    public void Built_in_aliases_are_valid()
    {
        AppAliasTable.BuiltIn.Validate().ShouldBeEmpty();
        AppAliasTable.BuiltIn.Aliases.Count.ShouldBeGreaterThan(10);
    }

    [Theory]
    [InlineData("outlook.exe")]
    [InlineData("olk.exe")]
    [InlineData("OLK.EXE")]
    public void Both_outlook_executables_map_to_the_same_identity(string exe)
    {
        Resolve(AppAliasTable.BuiltIn, Window(exe)).ShouldBe(new AppIdentity("outlook", "Outlook"));
    }

    [Theory]
    [InlineData("ms-teams.exe", "teams")]
    [InlineData("Teams.exe", "teams")]
    [InlineData("Code.exe", "vscode")]
    [InlineData("devenv.exe", "visual-studio")]
    [InlineData("rider64.exe", "rider")]
    [InlineData("msedge.exe", "edge")]
    [InlineData("explorer.exe", "explorer")]
    [InlineData("WindowsTerminal.exe", "terminal")]
    [InlineData("petrel.exe", "petrel")]
    public void Common_apps_have_built_in_aliases(string exe, string key)
    {
        Resolve(AppAliasTable.BuiltIn, Window(exe)).Key.ShouldBe(key);
    }

    [Fact]
    public void An_unknown_exe_falls_back_to_its_file_description()
    {
        Resolve(AppAliasTable.BuiltIn, Window("Acme.Tool.exe", description: "Acme Tool"))
            .ShouldBe(new AppIdentity("acme.tool", "Acme Tool"));
    }

    [Fact]
    public void An_unknown_exe_without_description_falls_back_to_its_name()
    {
        Resolve(AppAliasTable.BuiltIn, Window("FooBar.exe")).ShouldBe(new AppIdentity("foobar", "FooBar"));
    }

    [Fact]
    public void The_exe_name_is_taken_from_the_path_when_missing()
    {
        var window = new WindowSnapshot(1, @"C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE", "", "Inbox");

        Resolve(AppAliasTable.BuiltIn, window).Key.ShouldBe("outlook");
    }

    [Fact]
    public void A_window_without_any_exe_is_unknown()
    {
        Resolve(AppAliasTable.BuiltIn, new WindowSnapshot(1, "", "", "?")).ShouldBe(AppIdentity.Unknown);
    }

    [Fact]
    public void The_file_description_does_not_override_an_alias()
    {
        // Real Code.exe reports "Visual Studio Code"; our test window has the same, but an alias wins anyway.
        Resolve(AppAliasTable.BuiltIn, Code() with { FileDescription = "Electron" })
            .ShouldBe(new AppIdentity("vscode", "Visual Studio Code"));
    }

    [Fact]
    public void A_user_override_beats_a_built_in_alias()
    {
        // The user wants new Outlook tracked separately from classic Outlook.
        var table = AppAliasTable.BuiltIn.WithOverrides([new AppAlias("new-outlook", "New Outlook", ["olk.exe"])]);

        Resolve(table, Window("olk.exe")).ShouldBe(new AppIdentity("new-outlook", "New Outlook"));
        Resolve(table, Window("outlook.exe")).ShouldBe(new AppIdentity("outlook", "Outlook"));
        table.Validate().ShouldBeEmpty();
    }

    [Fact]
    public void A_user_override_can_rename_a_built_in_app()
    {
        var table = AppAliasTable.BuiltIn.WithOverrides([new AppAlias("outlook", "Mail", [])]);

        Resolve(table, Window("olk.exe")).ShouldBe(new AppIdentity("outlook", "Mail"));
        Resolve(table, Window("outlook.exe")).ShouldBe(new AppIdentity("outlook", "Mail"));
        table.Validate().ShouldBeEmpty();
    }

    [Fact]
    public void A_user_override_can_add_executables_to_a_built_in_app()
    {
        var table = AppAliasTable.BuiltIn.WithOverrides([new AppAlias("vscode", "", ["cursor.exe"])]);

        Resolve(table, Window("cursor.exe")).ShouldBe(new AppIdentity("vscode", "Visual Studio Code"));
        Resolve(table, Window("code.exe")).Key.ShouldBe("vscode");
    }

    [Fact]
    public void A_user_override_can_add_a_new_app()
    {
        var table = AppAliasTable.BuiltIn.WithOverrides([new AppAlias("acme", "Acme Suite", ["acme.exe", "acmeviewer.exe"])]);

        Resolve(table, Window("AcmeViewer.exe")).ShouldBe(new AppIdentity("acme", "Acme Suite"));
    }

    [Fact]
    public void Title_rules_win_over_the_exe_alias_when_the_title_matches()
    {
        var table = AppAliasTable.BuiltIn.WithOverrides(
            [new AppAlias("teams", "Microsoft Teams", ["msedge.exe", "chrome.exe"], @"\| Microsoft Teams")]);

        Resolve(table, Window("msedge.exe", "Chat | Microsoft Teams")).Key.ShouldBe("teams");
        Resolve(table, Window("msedge.exe", "GitHub")).Key.ShouldBe("edge");
        Resolve(table, Window("msedge.exe", title: null)).Key.ShouldBe("edge");
        table.Validate().ShouldBeEmpty();
    }

    [Fact]
    public void An_invalid_title_pattern_is_ignored_and_reported()
    {
        var table = AppAliasTable.BuiltIn.WithOverrides([new AppAlias("broken", "Broken", ["msedge.exe"], "([")]);

        Resolve(table, Window("msedge.exe", "([")).Key.ShouldBe("edge");
        table.Validate().ShouldHaveSingleItem().ShouldContain("not a valid regex");
    }

    [Fact]
    public void An_empty_override_list_returns_the_same_table()
    {
        AppAliasTable.BuiltIn.WithOverrides([]).ShouldBeSameAs(AppAliasTable.BuiltIn);
    }

    [Fact]
    public void The_resolver_table_can_be_swapped_at_runtime()
    {
        var resolver = new AliasAppIdentityResolver(AppAliasTable.Empty);
        resolver.Resolve(Window("olk.exe")).Key.ShouldBe("olk");

        resolver.Table = AppAliasTable.BuiltIn;

        resolver.Resolve(Window("olk.exe")).Key.ShouldBe("outlook");
    }

    [Theory]
    [InlineData("""{ "key": "Outlook", "name": "Outlook", "exes": ["a.exe"] }""", "key must be lowercase")]
    [InlineData("""{ "key": "a", "name": " ", "exes": ["a.exe"] }""", "name is required")]
    [InlineData("""{ "key": "a", "name": "A", "exes": [] }""", "at least one executable")]
    [InlineData("""{ "key": "a", "name": "A", "exes": ["a"] }""", "not an executable file name")]
    [InlineData("""{ "key": "a", "name": "A", "exes": ["C:\\a.exe"] }""", "not an executable file name")]
    [InlineData("""{ "key": "a", "name": "A", "exes": ["a.exe"] }, { "key": "b", "name": "B", "exes": ["A.EXE"] }""", "already used")]
    [InlineData("""{ "key": "a", "name": "A", "exes": ["a.exe"] }, { "key": "a", "name": "Other", "exes": ["b.exe"], "titlePattern": "x" }""", "differs from")]
    public void Validation_catches_bad_entries(string apps, string error)
    {
        var table = AppAliasTable.Parse($$"""{ "apps": [ {{apps}} ] }""");

        table.Validate().ShouldContain(e => e.Contains(error, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "apps": [ { "key": "a", "name": "A", "exe": ["a.exe"] } ] }""")] // typo
    [InlineData("""{ "apps": [ { "key": "a", "exes": ["a.exe"] } ] }""")]              // missing name
    [InlineData("""{ "apps": [ { "key": null, "name": "A", "exes": ["a.exe"] } ] }""")]
    [InlineData("""{ "aps": [] }""")]
    [InlineData("null")]
    public void Parsing_rejects_json_that_does_not_match_the_schema(string json)
    {
        Should.Throw<JsonException>(() => AppAliasTable.Parse(json));
    }

    [Fact]
    public void Parsing_allows_comments_and_trailing_commas()
    {
        var table = AppAliasTable.Parse("""
            {
              // my apps
              "apps": [ { "key": "a", "name": "A", "exes": [ "a.exe", ], }, ],
            }
            """);

        table.Aliases.ShouldHaveSingleItem().Exes.ShouldBe(["a.exe"]);
    }
}
