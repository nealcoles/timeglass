using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Timeglass.App;

/// <summary>
/// Makes sure only one Timeglass runs per user session, and lets a second launch ask the first
/// one to open the dashboard.
/// </summary>
/// <remarks>
/// <para>
/// The first instance owns a named mutex in the session's <c>Local\</c> namespace. A later
/// launch finds the mutex taken, connects to the first instance's named pipe, sends
/// <see cref="ActivateMessage"/> and exits.
/// </para>
/// <para>
/// The pipe is created with <see cref="PipeOptions.CurrentUserOnly"/> on both ends, so other users
/// can neither connect to it nor impersonate it, and its name includes the session id so the
/// same user signed in twice (console and remote desktop) gets two independent instances.
/// </para>
/// </remarks>
internal sealed partial class SingleInstance : IDisposable
{
    public const string DefaultName = "Timeglass";
    public const string ActivateMessage = "activate";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;

    /// <summary>Claims the instance name. Check <see cref="IsFirst"/> afterwards.</summary>
    public SingleInstance(string name = DefaultName, ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;

        using var process = Process.GetCurrentProcess();
        _mutex = new Mutex(initiallyOwned: false, $@"Local\{name}.SingleInstance", out var createdNew);
        IsFirst = createdNew;
        _pipeName = $"{name}.Activate.{Environment.UserName}.{process.SessionId}";
    }

    /// <summary>True if no other instance with this name is running in the session.</summary>
    public bool IsFirst { get; }

    /// <summary>
    /// Listens for later launches in the background and calls <paramref name="onActivate"/> on a
    /// thread-pool thread for each one. Only for the first instance.
    /// </summary>
    public void StartListening(Action onActivate)
    {
        ArgumentNullException.ThrowIfNull(onActivate);
        if (!IsFirst)
        {
            throw new InvalidOperationException("Only the first instance listens.");
        }

        _listener ??= Task.Run(() => ListenAsync(onActivate, _stop.Token));
    }

    /// <summary>Asks the first instance to open the dashboard. False if it couldn't be reached.</summary>
    public bool SignalFirst()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(ConnectTimeout);
            using var writer = new StreamWriter(client);
            writer.WriteLine(ActivateMessage);
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            // The first instance is still starting or is shutting down; this launch just exits.
            LogSignalFailed(_logger, ex);
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listener?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Logged by the listener.
        }

        _stop.Dispose();
        _mutex.Dispose();
    }

    private async Task ListenAsync(Action onActivate, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await using (server.ConfigureAwait(false))
                {
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    using var reader = new StreamReader(server);
                    var message = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (message == ActivateMessage)
                    {
                        onActivate();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A bad connection or a failing callback must not end the listener.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogListenFailed(_logger, ex);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not reach the running instance")]
    private static partial void LogSignalFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Single-instance pipe failed; listening again")]
    private static partial void LogListenFailed(ILogger logger, Exception exception);
}
