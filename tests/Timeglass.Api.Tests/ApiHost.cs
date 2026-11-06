using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Timeglass.Core.Domain;
using Timeglass.Core.Ports;
using Timeglass.Storage;

namespace Timeglass.Api.Tests;

/// <summary>
/// <see cref="TimeglassApi.MapTimeglassApi"/> on an in-memory server, reading from a real
/// SQLite database in a temp folder that is seeded with <c>seed</c> and deleted on dispose.
/// </summary>
/// <remarks>
/// The API is a library without an entry point of its own, so instead of
/// <c>WebApplicationFactory</c> the test builds the same <see cref="WebApplication"/> a host
/// would, on <see cref="TestServer"/>.
/// </remarks>
public sealed class ApiHost : IAsyncDisposable
{
    public static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "timeglass-tests", Guid.NewGuid().ToString("N"));
    private TimeglassDatabase? _database;
    private IntervalRepository? _repository;
    private WebApplication? _app;

    private ApiHost(DateTimeOffset now, ApiSecurityOptions? security)
    {
        Security = security;
        Time = new FakeTimeProvider(now);
        Time.SetLocalTimeZone(Oslo);
    }

    public FakeTimeProvider Time { get; }

    public FakeTrackingState Tracking { get; } = new();

    public HttpClient Client { get; private set; } = null!;

    /// <summary>The checks the API runs behind, or <see langword="null"/> to test the endpoints alone.</summary>
    public ApiSecurityOptions? Security { get; }

    /// <summary>Starts the API over a database holding <paramref name="seed"/>, with the clock at <paramref name="now"/>.</summary>
    public static Task<ApiHost> StartAsync(DateTimeOffset now, IEnumerable<ActivityInterval> seed, CancellationToken cancellationToken) =>
        StartAsync(now, seed, security: null, cancellationToken);

    /// <summary>
    /// As above, behind <see cref="ApiSecurity"/> when <paramref name="security"/> is set; the
    /// client then talks to <c>http://127.0.0.1:port/</c> and sends no token by itself.
    /// </summary>
    public static async Task<ApiHost> StartAsync(DateTimeOffset now, IEnumerable<ActivityInterval> seed, ApiSecurityOptions? security, CancellationToken cancellationToken)
    {
        var host = new ApiHost(now, security);
        try
        {
            await host.InitializeAsync(seed, cancellationToken);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    public async Task<T> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(new Uri(url, UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, cancellationToken))!;
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (_repository is not null)
        {
            await _repository.DisposeAsync();
        }

        _database?.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private async Task InitializeAsync(IEnumerable<ActivityInterval> seed, CancellationToken cancellationToken)
    {
        _database = TimeglassDatabase.Open(Path.Combine(_folder, "timeglass.db"));
        _repository = new IntervalRepository(_database);
        await _repository.ApplyAsync(
            [.. seed.Select(i => new IntervalChange(i.IsOpen ? IntervalChangeKind.Opened : IntervalChangeKind.Closed, i))],
            cancellationToken);
        await _repository.FlushAsync(cancellationToken);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(Time);
        builder.Services.AddSingleton<IReportingQueries>(_repository);
        builder.Services.AddSingleton<ITrackingState>(Tracking);
        builder.Services.AddTimeglassApi();
        if (Security is not null)
        {
            builder.Services.AddTimeglassApiSecurity(Security);
        }

        _app = builder.Build();
        if (Security is not null)
        {
            _app.UseTimeglassApiSecurity();
            _app.MapTimeglassSignIn();
        }

        _app.MapTimeglassApi();
        await _app.StartAsync(cancellationToken);
        Client = _app.GetTestClient();
        if (Security is not null)
        {
            Client.BaseAddress = new Uri($"http://127.0.0.1:{Security.Port}/");
        }
    }

    public static DateTimeOffset Utc(string at) => DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    public static ActivityInterval Active(string start, string end, AppIdentity app, string? title, bool open = false) =>
        new(Guid.CreateVersion7(), IntervalKind.Active, Utc(start), Utc(end), open, app, $@"C:\{app.Key}.exe", title);

    public static ActivityInterval Away(string start, string end, bool open = false) =>
        new(Guid.CreateVersion7(), IntervalKind.Away, Utc(start), Utc(end), open, null, null, null);
}

public sealed class FakeTrackingState : ITrackingState
{
    public bool IsPaused { get; set; }

    public DateTimeOffset? PausedUntil { get; set; }
}
