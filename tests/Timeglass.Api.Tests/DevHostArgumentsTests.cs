using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using static Timeglass.Api.Tests.ApiHost;

namespace Timeglass.Api.Tests;

/// <summary>
/// The dev host built from its command line, on a real port, as the end-to-end tests start it:
/// a stopped clock in a given zone, and the built dashboard served at <c>/</c>.
/// </summary>
public sealed class DevHostArgumentsTests : IAsyncLifetime
{
    private readonly string _web = Path.Combine(Path.GetTempPath(), "timeglass-tests", Guid.NewGuid().ToString("N"));
    private WebApplication? _app;
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_web);
        File.WriteAllText(Path.Combine(_web, "index.html"), "<!doctype html><title>Timeglass</title>");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync(Ct);
            await _app.DisposeAsync();
        }

        Directory.Delete(_web, recursive: true);
    }

    private async Task StartAsync(params string[] args)
    {
        _app = DevHost.DevHost.Build(["--port", "0", "--logging:logLevel:default", "warning", .. args]);
        await _app.StartAsync(Ct);
        var address = _app.Urls.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    private async Task<T> GetAsync<T>(string url)
    {
        using var response = await _client.GetAsync(new Uri(url, UriKind.Relative), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, Ct))!;
    }

    [Fact]
    public async Task Now_and_time_zone_stop_the_clock()
    {
        await StartAsync("--now", "2026-11-05T23:30:00Z", "--timeZone", "Europe/Oslo");

        // 00:30 on Friday in Oslo, still Thursday in UTC.
        var status = await GetAsync<StatusDto>("/api/status");
        status.Today.ShouldBe(new DateOnly(2026, 11, 6));

        await Task.Delay(TimeSpan.FromMilliseconds(50), Ct);
        var timeline = await GetAsync<TimelineDto>("/api/days/2026-11-06/timeline");
        timeline.Blocks.ShouldAllBe(b => b.End <= Utc("2026-11-05T23:30:00Z"));
    }

    [Fact]
    public async Task Web_serves_the_dashboard_next_to_the_api()
    {
        await StartAsync("--web", _web);

        var page = await _client.GetStringAsync(new Uri("/day/2026-11-05", UriKind.Relative), Ct);
        page.ShouldContain("<title>Timeglass</title>");

        var status = await GetAsync<StatusDto>("/api/status");
        status.Today.ShouldNotBe(default);
    }

    [Fact]
    public async Task Without_web_only_the_api_is_served()
    {
        await StartAsync();

        using var response = await _client.GetAsync(new Uri("/day/2026-11-05", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void Web_without_a_build_fails_at_startup()
    {
        var missing = Path.Combine(_web, "missing");

        Should.Throw<DirectoryNotFoundException>(() => DevHost.DevHost.Build(["--port", "0", "--web", missing]))
            .Message.ShouldContain("npm run build");
    }
}
