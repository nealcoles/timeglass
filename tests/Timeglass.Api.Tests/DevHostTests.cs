using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Timeglass.Core.Domain;
using Timeglass.DevHost;
using static Timeglass.Api.Tests.ApiHost;

namespace Timeglass.Api.Tests;

/// <summary>The dev host started as <c>dotnet run</c> would, with the clock fixed so the demo data is known.</summary>
public sealed class DevHostTests : IAsyncLifetime
{
    // Thursday 5 November 2026, 14:00 in Oslo.
    private static readonly DateTimeOffset Now = Utc("2026-11-05T13:00Z");

    private WebApplicationFactory<DevHostOptions> _factory = null!;
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        var time = new FakeTimeProvider(Now);
        time.SetLocalTimeZone(Oslo);
        _factory = new WebApplicationFactory<DevHostOptions>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(time));
        });
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<T> GetAsync<T>(string url)
    {
        using var response = await _client.GetAsync(new Uri(url, UriKind.Relative), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, Ct))!;
    }

    [Fact]
    public async Task Status_shows_tracking_in_an_app()
    {
        var status = await GetAsync<StatusDto>("/api/status");

        status.State.ShouldBe(TrackingStateKind.Active);
        status.CurrentApp.ShouldNotBeNull();
        status.Today.ShouldBe(new DateOnly(2026, 11, 5));
    }

    [Fact]
    public async Task Serves_the_demo_data()
    {
        var expected = DemoData.Generate(Now, Oslo);

        var week = await GetAsync<RangeSummaryDto>("/api/range/summary?from=2026-10-29&to=2026-11-05");

        week.Days.Select(d => d.ActiveMs > 0).ShouldBe([true, true, false, false, true, true, true, true]);
        week.Apps.Select(a => a.App.Key).Order().ShouldBe(["edge", "outlook", "teams", "terminal", "vscode"]);
        week.ActiveMs.ShouldBe(expected.Where(i => i.Kind == IntervalKind.Active).Sum(i => (long)(i.End - i.Start).TotalMilliseconds));
    }

    [Fact]
    public async Task Today_runs_up_to_now()
    {
        var timeline = await GetAsync<TimelineDto>("/api/days/2026-11-05/timeline");

        timeline.Blocks[^1].IsOpen.ShouldBeTrue();
        timeline.Blocks[^1].End.ShouldBe(Now);
    }

    [Fact]
    public async Task Serves_the_openapi_document()
    {
        using var response = await _client.GetAsync(new Uri(TimeglassApi.OpenApiPath, UriKind.Relative), Ct);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public void A_temp_database_is_deleted_on_dispose()
    {
        string path;
        using (var database = new DevDatabase(Options.Create(new DevHostOptions())))
        {
            database.IsNew.ShouldBeTrue();
            database.IsTemporary.ShouldBeTrue();
            path = database.Database.Path;
            File.Exists(path).ShouldBeTrue();
        }

        Directory.Exists(Path.GetDirectoryName(path)).ShouldBeFalse();
    }

    [Fact]
    public void A_given_database_is_kept_and_only_seeded_when_new()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timeglass-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "demo.db");
        try
        {
            var options = Options.Create(new DevHostOptions { Database = path });
            using (var database = new DevDatabase(options))
            {
                database.IsNew.ShouldBeTrue();
                database.IsTemporary.ShouldBeFalse();
            }

            File.Exists(path).ShouldBeTrue();
            using var again = new DevDatabase(options);
            again.IsNew.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
