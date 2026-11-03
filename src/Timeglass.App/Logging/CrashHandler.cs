using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Timeglass.App.Logging;

/// <summary>
/// Logs exceptions nothing else caught, and ends the app cleanly where it still can.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>An exception on the UI thread (<see cref="Application.ThreadException"/>, with
/// <see cref="UnhandledExceptionMode.CatchException"/> set in <c>Main</c>) is logged and the app
/// quits the normal way: the tray closes and the host stops, which closes and stores the open
/// interval.</item>
/// <item>An unobserved task exception is logged and marked observed; it doesn't end the app.</item>
/// <item>An exception on any other thread (<see cref="AppDomain.UnhandledException"/>) ends the
/// process whatever the handler does. It is logged; the interval it leaves open is closed by
/// crash recovery on the next start.</item>
/// </list>
/// The log file is written through on every entry, so the entry survives the process ending.
/// </remarks>
internal sealed partial class CrashHandler : IDisposable
{
    private readonly ILogger _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private bool _installed;

    public CrashHandler(ILogger<CrashHandler> logger, IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _lifetime = lifetime;
    }

    /// <summary>True once an unhandled exception was reported; <c>Main</c> then exits with 1.</summary>
    public bool HasCrashed { get; private set; }

    /// <summary>Subscribes to the process-wide events. Call once, on the UI thread, after the host is built.</summary>
    public void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public void Dispose()
    {
        if (!_installed)
        {
            return;
        }

        _installed = false;
        Application.ThreadException -= OnThreadException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    /// <summary>Logs an exception from the UI thread and asks the app to quit.</summary>
    internal void HandleUiThreadException(Exception exception)
    {
        HasCrashed = true;
        TryLog(() => LogUiThreadException(_logger, exception));
        _lifetime.StopApplication();
    }

    /// <summary>Logs an exception from another thread. The process ends if <paramref name="isTerminating"/>.</summary>
    internal void HandleUnhandledException(object exceptionObject, bool isTerminating)
    {
        HasCrashed = true;
        var exception = exceptionObject as Exception
            ?? new InvalidOperationException($"Non-exception object thrown: {exceptionObject}");
        TryLog(() => LogUnhandledException(_logger, exception, isTerminating));
    }

    /// <summary>Logs a faulted task nobody awaited.</summary>
    internal void HandleUnobservedTaskException(AggregateException exception) =>
        TryLog(() => LogUnobservedTaskException(_logger, exception));

    private void OnThreadException(object? sender, ThreadExceptionEventArgs e) =>
        HandleUiThreadException(e.Exception);

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) =>
        HandleUnhandledException(e.ExceptionObject, e.IsTerminating);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HandleUnobservedTaskException(e.Exception);
        e.SetObserved();
    }

    private static void TryLog(Action log)
    {
        try
        {
            log();
        }
#pragma warning disable CA1031 // A crash handler must not throw; late in shutdown the log may be closed already.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception on the UI thread; quitting")]
    private static partial void LogUiThreadException(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception (terminating: {IsTerminating})")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception, bool isTerminating);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unobserved task exception")]
    private static partial void LogUnobservedTaskException(ILogger logger, Exception exception);
}
