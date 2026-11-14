using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Timeglass.Core.Domain;
using Timeglass.Core.Settings;
using static Timeglass.Api.Tests.ApiHost;

namespace Timeglass.Api.Tests;

/// <summary><c>GET /api/export</c> (#39) against a real, seeded database.</summary>
public sealed class ExportTests : IAsyncLifetime
{
    private const string Range = "from=2026-11-04&to=2026-11-05";
    private const string IntervalHeader = "date,start,end,duration_ms,kind,app_key,app,title";

    private static readonly AppIdentity Code = new("code", "Code");
    private static readonly AppIdentity Outlook = new("outlook", "Outlook");

    // Wednesday 4 and Thursday 5 November 2026 in Oslo (UTC+1), with a late evening in between.
    // Now is 10:00 local on Thursday, with Code open since 09:30.
    private static readonly ActivityInterval[] Seed =
    [
        Active("2026-11-04T07:00Z", "2026-11-04T08:00Z", Code, "main.cs"),
        Active("2026-11-04T08:00Z", "2026-11-04T08:30Z", Outlook, "Inbox"),
        Away("2026-11-04T08:30Z", "2026-11-04T09:00Z"),
        Active("2026-11-04T09:00Z", "2026-11-04T09:15Z", Code, null),
        Active("2026-11-04T22:00Z", "2026-11-05T00:00Z", Code, "late.cs"),
        Active("2026-11-05T07:00Z", "2026-11-05T08:30Z", Outlook, "RE: Budget"),
        Active("2026-11-05T08:30Z", "2026-11-05T08:40Z", Code, "main.cs", open: true),
    ];

    private static readonly DateTimeOffset Now = Utc("2026-11-05T09:00Z");

    private ApiHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await StartAsync(Now, Seed, Ct);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private static long Minutes(double minutes) => (long)TimeSpan.FromMinutes(minutes).TotalMilliseconds;

    private static async Task<string[]> CsvLinesAsync(ApiHost host, string query)
    {
        var csv = await host.Client.GetStringAsync(new Uri($"/api/export?{query}&format=csv", UriKind.Relative), Ct);
        csv.ShouldEndWith("\r\n");
        return csv[..^2].Split("\r\n");
    }

    private Task<string[]> CsvLinesAsync(string query) => CsvLinesAsync(_host, query);

    private Task<JsonNode> JsonAsync(string query) => _host.GetJsonAsync<JsonNode>($"/api/export?{query}", Ct);

    [Fact]
    public async Task Json_has_one_row_per_interval_in_time_order()
    {
        var export = await JsonAsync(Range);

        export["from"]!.GetValue<string>().ShouldBe("2026-11-04");
        export["to"]!.GetValue<string>().ShouldBe("2026-11-05");
        export["granularity"]!.GetValue<string>().ShouldBe("intervals");
        export["dailyApps"].ShouldBeNull();
        export["intervals"]!.AsArray()
            .Select(i => $"{i!["kind"]} {i["app"]?["key"]} {i["title"]} {i["start"]} {i["durationMs"]!.GetValue<long>() / 60000}{(i["isOpen"]!.GetValue<bool>() ? "*" : "")}")
            .ShouldBe(
            [
                "active code main.cs 2026-11-04T08:00:00+01:00 60",
                "active outlook Inbox 2026-11-04T09:00:00+01:00 30",
                "away   2026-11-04T09:30:00+01:00 30",
                "active code  2026-11-04T10:00:00+01:00 15",
                "active code late.cs 2026-11-04T23:00:00+01:00 120",
                "active outlook RE: Budget 2026-11-05T08:00:00+01:00 90",
                "active code main.cs 2026-11-05T09:30:00+01:00 30*", // open: until now
            ]);

        var first = export["intervals"]![0]!;
        first["app"]!["name"]!.GetValue<string>().ShouldBe("Code");
        first["end"]!.GetValue<string>().ShouldBe("2026-11-04T09:00:00+01:00");
        first["durationMs"]!.GetValue<long>().ShouldBe(Minutes(60));
        export["intervals"]![2]!["app"].ShouldBeNull();
        export["intervals"]![3]!["title"].ShouldBeNull();
    }

    [Fact]
    public async Task Intervals_are_clipped_to_the_days_asked_for()
    {
        var export = await JsonAsync("from=2026-11-05&to=2026-11-05");

        // The late evening started at 23:00 the day before.
        var first = export["intervals"]![0]!;
        first["title"]!.GetValue<string>().ShouldBe("late.cs");
        first["start"]!.GetValue<string>().ShouldBe("2026-11-05T00:00:00+01:00");
        first["durationMs"]!.GetValue<long>().ShouldBe(Minutes(60));
        export["intervals"]!.AsArray().Count.ShouldBe(3);
    }

    [Fact]
    public async Task Json_by_day_and_app_agrees_with_the_day_summaries()
    {
        var export = await JsonAsync($"{Range}&granularity=daily-apps");

        export["granularity"]!.GetValue<string>().ShouldBe("daily-apps");
        export["intervals"].ShouldBeNull();
        var rows = export["dailyApps"]!.AsArray()
            .Select(r => (Date: r!["date"]!.GetValue<string>(), Key: r["app"]!["key"]!.GetValue<string>(), Name: r["app"]!["name"]!.GetValue<string>(), Ms: r["durationMs"]!.GetValue<long>()))
            .ToList();
        rows.ShouldBe(
        [
            ("2026-11-04", "code", "Code", Minutes(135)),
            ("2026-11-04", "outlook", "Outlook", Minutes(30)),
            ("2026-11-05", "code", "Code", Minutes(90)),
            ("2026-11-05", "outlook", "Outlook", Minutes(90)),
        ]);

        foreach (var date in new[] { "2026-11-04", "2026-11-05" })
        {
            var day = await _host.GetJsonAsync<DaySummaryDto>($"/api/days/{date}/summary", Ct);
            rows.Where(r => r.Date == date).Select(r => (r.Key, r.Ms)).ShouldBe(day.Apps.Select(a => (a.App.Key, a.DurationMs)));
        }
    }

    [Fact]
    public async Task Csv_has_a_header_and_one_line_per_interval()
    {
        (await CsvLinesAsync(Range)).ShouldBe(
        [
            IntervalHeader,
            "2026-11-04,2026-11-04T08:00:00.000+01:00,2026-11-04T09:00:00.000+01:00,3600000,active,code,Code,main.cs",
            "2026-11-04,2026-11-04T09:00:00.000+01:00,2026-11-04T09:30:00.000+01:00,1800000,active,outlook,Outlook,Inbox",
            "2026-11-04,2026-11-04T09:30:00.000+01:00,2026-11-04T10:00:00.000+01:00,1800000,away,,,",
            "2026-11-04,2026-11-04T10:00:00.000+01:00,2026-11-04T10:15:00.000+01:00,900000,active,code,Code,",
            "2026-11-04,2026-11-04T23:00:00.000+01:00,2026-11-05T01:00:00.000+01:00,7200000,active,code,Code,late.cs",
            "2026-11-05,2026-11-05T08:00:00.000+01:00,2026-11-05T09:30:00.000+01:00,5400000,active,outlook,Outlook,RE: Budget",
            "2026-11-05,2026-11-05T09:30:00.000+01:00,2026-11-05T10:00:00.000+01:00,1800000,active,code,Code,main.cs",
        ]);
    }

    [Fact]
    public async Task Csv_by_day_and_app()
    {
        (await CsvLinesAsync($"{Range}&granularity=daily-apps")).ShouldBe(
        [
            "date,app_key,app,duration_ms",
            "2026-11-04,code,Code,8100000",
            "2026-11-04,outlook,Outlook,1800000",
            "2026-11-05,code,Code,5400000",
            "2026-11-05,outlook,Outlook,5400000",
        ]);
    }

    [Theory]
    [InlineData("Budget, final", "\"Budget, final\"")]
    [InlineData("The \"real\" plan", "\"The \"\"real\"\" plan\"")]
    [InlineData("first line\nsecond line", "\"first line\nsecond line\"")]
    [InlineData("first line\r\nsecond, \"quoted\" line", "\"first line\r\nsecond, \"\"quoted\"\" line\"")]
    [InlineData(" padded ", "\" padded \"")]
    [InlineData("Blåbærsyltetøy – 日本語", "Blåbærsyltetøy – 日本語")]
    [InlineData("a;b\tc", "a;b\tc")]
    public async Task Csv_quotes_titles_with_commas_quotes_and_line_breaks(string title, string expected)
    {
        await using var host = await StartAsync(Now, [Active("2026-11-04T07:00Z", "2026-11-04T08:00Z", Code, title)], Ct);

        var csv = await host.Client.GetStringAsync(new Uri("/api/export?from=2026-11-04&to=2026-11-04&format=csv", UriKind.Relative), Ct);

        csv.ShouldBe($"{IntervalHeader}\r\n2026-11-04,2026-11-04T08:00:00.000+01:00,2026-11-04T09:00:00.000+01:00,3600000,active,code,Code,{expected}\r\n");
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://example.invalid\",\"open\")", "\"'=HYPERLINK(\"\"http://example.invalid\"\",\"\"open\"\")\"")]
    [InlineData("+1+1", "\"'+1+1\"")]
    [InlineData("-2+3", "\"'-2+3\"")]
    [InlineData("@SUM(A1:A9)", "\"'@SUM(A1:A9)\"")]
    [InlineData("\t=1+1", "\"'\t=1+1\"")]
    [InlineData("\r=1+1", "\"'\r=1+1\"")]
    [InlineData("\n=1+1", "\"'\n=1+1\"")]
    [InlineData("＝1+1", "\"'＝1+1\"")]
    [InlineData("a=1", "a=1")] // only at the start
    public async Task Csv_keeps_a_spreadsheet_from_running_a_title_as_a_formula(string title, string expected)
    {
        // An app's name comes from its executable, so it gets the same treatment.
        var app = new AppIdentity("calc", "=cmd");
        await using var host = await StartAsync(Now, [Active("2026-11-04T07:00Z", "2026-11-04T08:00Z", app, title)], Ct);

        var csv = await host.Client.GetStringAsync(new Uri("/api/export?from=2026-11-04&to=2026-11-04&format=csv", UriKind.Relative), Ct);
        csv.ShouldBe($"{IntervalHeader}\r\n2026-11-04,2026-11-04T08:00:00.000+01:00,2026-11-04T09:00:00.000+01:00,3600000,active,calc,\"'=cmd\",{expected}\r\n");

        // JSON has the title as it was recorded.
        var json = await host.GetJsonAsync<JsonNode>("/api/export?from=2026-11-04&to=2026-11-04", Ct);
        json["intervals"]![0]!["title"]!.GetValue<string>().ShouldBe(title);
    }

    [Fact]
    public async Task Csv_is_utf8_with_a_byte_order_mark_and_is_a_file_to_save()
    {
        using var response = await _host.Client.GetAsync(new Uri($"/api/export?{Range}&format=csv&granularity=daily-apps", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldBe(MediaTypeHeaderValue.Parse("text/csv; charset=utf-8"));
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName!.Trim('"').ShouldBe("timeglass-daily-apps-2026-11-04_2026-11-05.csv");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());
        Encoding.UTF8.GetString(bytes, 3, 4).ShouldBe("date");
    }

    [Fact]
    public async Task Json_is_a_file_to_save_too()
    {
        using var response = await _host.Client.GetAsync(new Uri($"/api/export?{Range}", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe("timeglass-intervals-2026-11-04_2026-11-05.json");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task An_empty_range_is_an_empty_export()
    {
        const string Empty = "from=2026-10-01&to=2026-10-31";

        (await CsvLinesAsync(Empty)).ShouldBe([IntervalHeader]);
        (await CsvLinesAsync($"{Empty}&granularity=daily-apps")).ShouldBe(["date,app_key,app,duration_ms"]);
        (await JsonAsync(Empty))["intervals"]!.AsArray().ShouldBeEmpty();
        (await JsonAsync($"{Empty}&granularity=daily-apps"))["dailyApps"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_range_can_be_longer_than_a_year()
    {
        (await CsvLinesAsync("from=2020-01-01&to=2026-11-05")).Length.ShouldBe(Seed.Length + 1);
        (await CsvLinesAsync("from=2020-01-01&to=2026-11-05&granularity=daily-apps")).Length.ShouldBe(5);
    }

    [Fact]
    public async Task Time_in_apps_that_are_excluded_now_is_left_out()
    {
        _host.Settings.Current = new TimeglassSettings { ExcludedApps = ["Outlook"] };

        var lines = await CsvLinesAsync(Range);
        lines.ShouldNotContain(l => l.Contains("outlook", StringComparison.OrdinalIgnoreCase) || l.Contains("Inbox") || l.Contains("Budget"));
        lines.Length.ShouldBe(6);

        (await CsvLinesAsync($"{Range}&granularity=daily-apps")).ShouldBe(
        [
            "date,app_key,app,duration_ms",
            "2026-11-04,code,Code,8100000",
            "2026-11-05,code,Code,5400000",
        ]);
        var json = await JsonAsync(Range);
        json["intervals"]!.AsArray().ShouldAllBe(i => i!["app"] == null || i["app"]!["key"]!.GetValue<string>() == "code");
        (await JsonAsync($"{Range}&granularity=daily-apps"))["dailyApps"]!.AsArray().Count.ShouldBe(2);
    }

    [Fact]
    public async Task Titles_of_apps_without_titles_are_left_out()
    {
        _host.Settings.Current = new TimeglassSettings { AppsWithoutTitles = ["outlook"] };

        var json = await JsonAsync(Range);
        json["intervals"]!.AsArray().Select(i => i!["title"]?.GetValue<string>())
            .ShouldBe(["main.cs", null, null, null, "late.cs", null, "main.cs"]);

        var lines = await CsvLinesAsync(Range);
        lines.ShouldContain(l => l.EndsWith(",active,outlook,Outlook,", StringComparison.Ordinal));
        lines.ShouldNotContain(l => l.Contains("Inbox") || l.Contains("Budget"));
    }

    [Fact]
    public async Task No_title_is_exported_while_titles_are_turned_off()
    {
        _host.Settings.Current = new TimeglassSettings { RecordWindowTitles = false };

        (await JsonAsync(Range))["intervals"]!.AsArray().ShouldAllBe(i => i!["title"] == null);
        (await CsvLinesAsync(Range)).Skip(1).ShouldAllBe(l => l.EndsWith(','));
    }

    [Fact]
    public async Task Deleted_time_is_not_exported()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri("/api/intervals?from=2026-11-04&to=2026-11-04", UriKind.Relative))
        {
            Content = JsonContent.Create(new { confirm = "2026-11-04..2026-11-04" }),
        };
        using var deleted = await _host.Client.SendAsync(request, Ct);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Left: the hour after midnight of the late evening, and the 5th.
        (await CsvLinesAsync(Range)).ShouldBe(
        [
            IntervalHeader,
            "2026-11-05,2026-11-05T00:00:00.000+01:00,2026-11-05T01:00:00.000+01:00,3600000,active,code,Code,late.cs",
            "2026-11-05,2026-11-05T08:00:00.000+01:00,2026-11-05T09:30:00.000+01:00,5400000,active,outlook,Outlook,RE: Budget",
            "2026-11-05,2026-11-05T09:30:00.000+01:00,2026-11-05T10:00:00.000+01:00,1800000,active,code,Code,main.cs",
        ]);
        (await JsonAsync($"{Range}&granularity=daily-apps"))["dailyApps"]!.AsArray().Select(r => r!["date"]!.GetValue<string>())
            .ShouldBe(["2026-11-05", "2026-11-05"]);
    }

    [Fact]
    public async Task The_executable_path_is_not_exported()
    {
        var json = await _host.Client.GetStringAsync(new Uri($"/api/export?{Range}", UriKind.Relative), Ct);
        var csv = await _host.Client.GetStringAsync(new Uri($"/api/export?{Range}&format=csv", UriKind.Relative), Ct);

        json.ShouldNotContain(".exe");
        csv.ShouldNotContain(".exe");
    }

    [Theory]
    [InlineData("/api/export", "from")]
    [InlineData("/api/export?from=2026-11-04", "to")]
    [InlineData("/api/export?from=04.11.2026&to=2026-11-05", "from")]
    [InlineData("/api/export?from=2026-11-04&to=2026-02-30", "to")]
    [InlineData("/api/export?from=2026-11-05&to=2026-11-04", "to")]
    [InlineData("/api/export?from=0001-01-01&to=2026-11-04", "from")]
    [InlineData("/api/export?from=2026-11-04&to=9999-12-31", "to")]
    [InlineData("/api/export?from=2026-11-04&to=2026-11-05&format=xml", "format")]
    [InlineData("/api/export?from=2026-11-04&to=2026-11-05&format=CSV", "format")]
    [InlineData("/api/export?from=2026-11-04&to=2026-11-05&granularity=hourly", "granularity")]
    public async Task Invalid_requests_are_a_problem_details_400(string url, string field)
    {
        using var response = await _host.Client.GetAsync(new Uri(url, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        response.Content.Headers.ContentDisposition.ShouldBeNull();
        var problem = await response.Content.ReadFromJsonAsync<JsonNode>(JsonSerializerOptions.Web, Ct);
        problem!["errors"]![field]!.AsArray().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_export_needs_the_access_token_and_a_request_from_the_dashboard()
    {
        var token = ApiAccessToken.Create();
        await using var host = await StartAsync(Now, Seed, new ApiSecurityOptions(5381, token), Ct);
        var url = new Uri($"/api/export?{Range}&format=csv", UriKind.Relative);

        using var anonymous = await host.Client.GetAsync(url, Ct);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.Content.ReadAsStringAsync(Ct)).ShouldNotContain("main.cs");

        using var fromAnotherSite = new HttpRequestMessage(HttpMethod.Get, url);
        fromAnotherSite.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        fromAnotherSite.Headers.Add("Origin", "https://evil.example");
        using var forbidden = await host.Client.SendAsync(fromAnotherSite, Ct);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var rebound = new HttpRequestMessage(HttpMethod.Get, url);
        rebound.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        rebound.Headers.Host = "evil.example:5381";
        using var badHost = await host.Client.SendAsync(rebound, Ct);
        badHost.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The dashboard's download link: a same-origin navigation with the cookie.
        using var link = new HttpRequestMessage(HttpMethod.Get, url);
        link.Headers.Add("Cookie", $"{ApiSecurity.CookieName}={token.Value}");
        link.Headers.Add("Sec-Fetch-Site", "same-origin");
        using var ok = await host.Client.SendAsync(link, Ct);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync(Ct)).ShouldContain("main.cs");
    }

    [Fact]
    public async Task A_large_export_arrives_complete()
    {
        // A month of one-minute intervals: more than the response buffers hold at once.
        var start = Utc("2026-10-01T00:00Z");
        var many = Enumerable.Range(0, 30 * 24 * 60).Select(i => new ActivityInterval(
            Guid.CreateVersion7(), IntervalKind.Active, start.AddMinutes(i), start.AddMinutes(i + 1), false, i % 2 == 0 ? Code : Outlook, null, $"Document {i}, draft"));
        await using var host = await StartAsync(Now, many, Ct);

        var lines = await CsvLinesAsync(host, "from=2026-09-30&to=2026-10-31");
        lines.Length.ShouldBe((30 * 24 * 60) + 1);
        lines[^1].ShouldEndWith(",active,outlook,Outlook,\"Document 43199, draft\"");

        var json = await host.GetJsonAsync<JsonNode>("/api/export?from=2026-09-30&to=2026-10-31", Ct);
        json["intervals"]!.AsArray().Count.ShouldBe(30 * 24 * 60);

        var daily = await CsvLinesAsync(host, "from=2026-09-30&to=2026-10-31&granularity=daily-apps");
        daily.Length.ShouldBe((31 * 2) + 1); // Oslo is ahead of UTC, so the month covers 31 local days
    }
}
