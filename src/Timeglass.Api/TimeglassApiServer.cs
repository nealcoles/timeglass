using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Timeglass.Api;

/// <summary>
/// Builds the API server a host embeds: Kestrel on 127.0.0.1 only, the checks of
/// <see cref="ApiSecurity"/>, the sign-in endpoint and <see cref="TimeglassApi"/>.
/// </summary>
public static class TimeglassApiServer
{
    /// <summary>
    /// Builds the server. The caller starts and stops it, and registers the API's data in
    /// <paramref name="addServices"/>: an <c>IReportingQueries</c>, an <see cref="ITrackingState"/>
    /// and, if it has one, its <see cref="TimeProvider"/>.
    /// </summary>
    /// <param name="security">The port to listen on (see <see cref="LoopbackPort.Choose"/>) and the token.</param>
    /// <param name="loggerFactory">The host's logging, so the server logs where the app does.</param>
    /// <param name="addServices">Registers the API's data.</param>
    public static WebApplication Build(ApiSecurityOptions security, ILoggerFactory loggerFactory, Action<IServiceCollection> addServices)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(addServices);

        // No args, appsettings or environment: nothing outside the app can change where it listens.
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(TimeglassApiServer).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;

            // Never Any or IPv6Any: the API is for this machine only.
            kestrel.Listen(IPAddress.Loopback, security.Port);
        });

        builder.Logging.ClearProviders();
        builder.Services.Replace(ServiceDescriptor.Singleton(loggerFactory));
        builder.Services.AddTimeglassApiSecurity(security);
        addServices(builder.Services);
        builder.Services.AddTimeglassApi();

        var app = builder.Build();
        app.UseTimeglassApiSecurity();
        app.MapTimeglassSignIn();
        app.MapTimeglassApi();
        return app;
    }
}
