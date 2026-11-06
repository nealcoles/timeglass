using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Timeglass.Core.Ports;
using Timeglass.Storage;

namespace Timeglass.Api.Tests;

/// <summary>The server a host embeds, on real Kestrel and a real port.</summary>
public sealed class ApiServerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Listens_on_127_0_0_1_only_and_needs_the_token()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timeglass-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await RunServerAsync(folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task RunServerAsync(string folder)
    {
        using var database = TimeglassDatabase.Open(Path.Combine(folder, "timeglass.db"));
        await using var repository = new IntervalRepository(database);
        var security = new ApiSecurityOptions(LoopbackPort.Choose(0), ApiAccessToken.Create());
        await using var app = TimeglassApiServer.Build(security, NullLoggerFactory.Instance, services =>
        {
            services.AddSingleton<IReportingQueries>(repository);
            services.AddSingleton<ITrackingState>(new FakeTrackingState());
        });
        await app.StartAsync(Ct);

        var addresses = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses;
        addresses.ShouldBe([$"http://127.0.0.1:{security.Port}"]);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{security.Port}/") };
        using (var anonymous = await client.GetAsync(new Uri("/api/status", UriKind.Relative), Ct))
        {
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            anonymous.Headers.Contains("Server").ShouldBeFalse();
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", security.Token.Value);
        using (var signedIn = await client.GetAsync(new Uri("/api/status", UriKind.Relative), Ct))
        {
            signedIn.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await app.StopAsync(Ct);
    }

    [Fact]
    public void A_free_preferred_port_is_kept()
    {
        var free = LoopbackPort.Choose(0);

        LoopbackPort.Choose(free).ShouldBe(free);
    }

    [Fact]
    public void A_taken_port_falls_back_to_a_free_one()
    {
        using var taken = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        taken.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        taken.Listen();
        var port = ((IPEndPoint)taken.LocalEndPoint!).Port;

        var chosen = LoopbackPort.Choose(port);

        chosen.ShouldNotBe(port);
        chosen.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Tokens_are_random_and_compared_exactly()
    {
        var token = ApiAccessToken.Create();

        token.Value.Length.ShouldBe(43);
        ApiAccessToken.Create().Value.ShouldNotBe(token.Value);
        token.Matches(token.Value).ShouldBeTrue();
        token.Matches(token.Value[..^1]).ShouldBeFalse();
        token.Matches(token.Value + "x").ShouldBeFalse();
        token.Matches(null).ShouldBeFalse();
        token.ToString().ShouldNotContain(token.Value);
    }

    [Fact]
    public void The_runtime_file_round_trips_and_is_deleted()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timeglass-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "nested", "api.json");
        try
        {
            ApiRuntimeFile.TryRead(path).ShouldBeNull();

            var info = new ApiRuntimeInfo(5381, "secret", 42);
            ApiRuntimeFile.Write(path, info);
            ApiRuntimeFile.Write(path, info with { Port = 5382 });

            var read = ApiRuntimeFile.TryRead(path).ShouldNotBeNull();
            read.ShouldBe(info with { Port = 5382 });
            read.BaseAddress.ShouldBe(new Uri("http://127.0.0.1:5382/"));
            Directory.GetFiles(Path.GetDirectoryName(path)!).ShouldBe([path]);

            ApiRuntimeFile.Delete(path);
            File.Exists(path).ShouldBeFalse();
            ApiRuntimeFile.Delete(path);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Deleting_in_a_missing_folder_is_fine()
    {
        ApiRuntimeFile.Delete(Path.Combine(Path.GetTempPath(), "timeglass-tests", Guid.NewGuid().ToString("N"), "api.json"));
    }

    [Fact]
    public void A_corrupt_runtime_file_reads_as_none()
    {
        var path = Path.Combine(Path.GetTempPath(), $"timeglass-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not json");
            ApiRuntimeFile.TryRead(path).ShouldBeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
