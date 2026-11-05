# Architecture

Timeglass is one Windows process: a tray app that watches the foreground window, turns what it
sees into time intervals, stores them in SQLite, and serves them to a React UI through a local
HTTP API. This page describes how the pieces fit together. The reasons behind the big choices
are in the [ADRs](adr/).

Parts marked _(planned)_ don't exist yet; the issue number says where they are being built.

## Projects and the dependency rule

```text
                  ┌──────────────────────┐
                  │    Timeglass.App     │  tray host, composition root (Windows)
                  └──────────┬───────────┘
        ┌────────────────────┼──────────────────────┐
        ▼                    ▼                      ▼
┌───────────────┐   ┌─────────────────┐   ┌──────────────────┐
│ Timeglass.    │   │ Timeglass.      │   │ Timeglass.Api    │
│ Windows       │   │ Storage         │   │ (ASP.NET Core)   │
│ (Win32)       │   │ (SQLite)        │   │                  │
└───────┬───────┘   └────────┬────────┘   └────────┬─────────┘
        └────────────────────┼─────────────────────┘
                             ▼
                  ┌──────────────────────┐
                  │    Timeglass.Core    │  domain, ports, tracking engine
                  └──────────────────────┘
```

| Project             | Target            | Responsibility                                                                                |
| ------------------- | ----------------- | --------------------------------------------------------------------------------------------- |
| `Timeglass.Core`    | `net10.0`         | Domain records, ports (interfaces), the tracking engine and service, app identity resolution. |
| `Timeglass.Storage` | `net10.0`         | SQLite database, migrations, `IntervalRepository` (implements `IIntervalSink`).               |
| `Timeglass.Api`     | `net10.0`         | Local HTTP API for the UI _(planned, #27)_.                                                   |
| `Timeglass.Windows` | `net10.0-windows` | Win32 implementations of the ports: foreground watcher, idle detector, session events.        |
| `Timeglass.App`     | `net10.0-windows` | `Timeglass.exe`: generic host, composition root, tray icon, single instance.                  |
| `web/`              | Node / browser    | React + TypeScript UI, built with Vite and served by the app _(planned, #30)_.                |

The rule:

1. **Core depends on nothing of ours.** It only knows its own types and the ports it defines.
   Everything that touches the outside world (OS, disk, network) is behind a port.
2. **Storage, Api and Windows depend only on Core**, never on each other.
3. **Only App knows all of them.** It is the composition root: it creates the concrete
   adapters and hands them to Core.
4. **Portable code never references Windows-only assemblies.** Core, Storage and Api must build
   and test on Linux (`Timeglass.Portable.slnx`), so CI can run most tests on the fast runner.

Rules 1 and 4 are enforced by `tests/Timeglass.Core.Tests/ArchitectureTests.cs`.

### Time

Core never reads the clock directly. All time goes through `TimeProvider`, and
`BannedSymbols.txt` makes `DateTime.Now`, `DateTimeOffset.UtcNow`, `Task.Delay(TimeSpan)` and
`System.Threading.Timer` build errors in Core. Tests use a fake time provider to run hours of
tracking in milliseconds.

## Data flow

```text
 Win32                       Core                                   Storage            UI
─────────               ─────────────────────────────────         ──────────        ───────
WinEvent hooks ─┐
+ polling       │  ForegroundChanged
                ├───────────────────►┌─────────┐   ┌───────────┐  IntervalChange   ┌────────┐
GetLastInput ───┤  Tick (1 s timer)  │ channel ├──►│ Tracking  ├──────────────────►│ SQLite │
Info            │                    └─────────┘   │ Engine    │  (upsert by id)   │  file  │
                │  SessionChanged                  └───────────┘                   └───┬────┘
SystemEvents ───┘                                                                      │
                                                                           queries     ▼
                                                    React UI ◄── HTTP (127.0.0.1) ── Api
```

1. **OS events.** `Timeglass.Windows` reports three things through Core's ports:
   - `IForegroundWindowSource`: the foreground window or its title changed. Implemented by
     `Win32ForegroundWindowSource` with `SetWinEventHook` (`EVENT_SYSTEM_FOREGROUND`,
     `EVENT_OBJECT_NAMECHANGE`) plus polling every 2 s to catch missed events. Each event carries
     a `WindowSnapshot` (process id, exe path and name, title, file description).
     `ProcessResolver` fills in the process details and caches them by process id and start
     time. UWP apps, whose windows belong to `ApplicationFrameHost.exe`, are resolved to the
     app's own process through the frame's `CoreWindow` child. Processes the user may not query
     (elevated or protected) get the exe name `unknown-elevated` and show up as "Elevated app"
     instead of failing.
   - `IIdleSource`: time of the last keyboard or mouse input.
   - `ISessionEventSource`: lock, unlock, suspend, resume, shutdown. `Win32SessionEventSource`
     listens to `SystemEvents`: a remote or console disconnect counts as a lock and a reconnect
     as an unlock; logoff and shutdown (`SessionEnding`) become `ShuttingDown`. It must be
     disposed, since the static `SystemEvents` otherwise keeps its handlers alive.
2. **Engine.** `TrackingService` turns those into `TrackingEvent`s (`ForegroundChanged`, `Tick`,
   `SessionChanged`, `TrackingStarted`, `TrackingStopped`) and feeds them to `TrackingEngine`,
   a pure state machine with no clock, I/O or locks. The engine:
   - maps each window to an `AppIdentity` through `IAppIdentityResolver` (the alias table in
     `Apps/AppAliases.json` merges e.g. classic and new Outlook);
   - ignores windows that stay in front for less than a second (Alt-Tab flicker), backdating
     the switch once it sticks so no time is lost;
   - closes the active interval at the last input time after 5 minutes without input, and on
     lock or sleep, and opens an `Away` interval;
   - applies the privacy settings: no interval at all while an app in `excludedApps` is in
     front, and no title for apps in `appsWithoutTitles` or when `recordWindowTitles` is off
     ([ADR 0005](adr/0005-privacy-gaps-not-intervals.md));
   - emits `IntervalChange`s (`Opened`, `Updated`, `Closed`), each carrying the full
     `ActivityInterval`. The open interval gets an `Updated` checkpoint every 15 s.

   The engine's XML docs list every rule in detail.

3. **Storage.** `IntervalRepository` implements `IIntervalSink` and upserts each change by
   interval id into the `intervals` table. Because every change carries the whole interval, a
   lost or failed write is repaired by the next checkpoint, and a crash loses at most one
   checkpoint interval. On startup, `TimeglassDatabase.Open` applies migrations and closes any
   interval left open by a crash at its last checkpoint. A unique index allows at most one
   open interval. `TrackingService.DeleteRangeAsync` deletes a time range (for the UI, through
   the API): it runs in order with the tracking events, closes the open interval first if the
   range reaches it, and the repository cuts intervals at the range edges with `secure_delete`
   on.
4. **API** _(planned, #27, #29)_. ASP.NET Core minimal API on `127.0.0.1`, reading intervals by
   time range and aggregating them (day summary, timeline, week). Loopback only, with Host
   header validation and an access token.
5. **UI** _(planned, #31 onward)_. React app served by the same process (#30). In development,
   Vite proxies `/api` to the backend.

### Storage model

Intervals, not samples (see [ADR 0002](adr/0002-sqlite-with-stored-intervals.md)). The schema
lives in numbered scripts in `src/Timeglass.Storage/Migrations/`, embedded in the assembly and
applied in order by `Migrator`.

| Table       | Columns (main)                                                                                                                   |
| ----------- | -------------------------------------------------------------------------------------------------------------------------------- |
| `apps`      | `id`, `key` (unique, e.g. `outlook`), `display_name`                                                                             |
| `intervals` | `id` (engine GUID), `start_utc`, `end_utc` (Unix ms), `kind` (0 active, 1 away), `app_id`, `exe_path`, `window_title`, `is_open` |

Intervals never overlap and `end_utc >= start_utc`. They are contiguous within a tracking run;
paused time and time in an excluded app are gaps. All times are UTC; converting to local
days (including DST) is a reporting concern (#25).

To change the schema, add the next numbered `.sql` file. Never edit a script that has shipped.

## The host

`Timeglass.App` builds a generic host (`Microsoft.Extensions.Hosting`) for dependency injection,
configuration, logging and lifetime. `TimeglassHost.AddTimeglass` is the composition root, the
only code that knows every project. The container validates the whole graph when it is built,
so a missing registration fails at startup and in `Timeglass.App.Tests`.

Startup, in `Program.Main` on the STA main thread:

1. **Single instance.** A named mutex in the session's `Local\` namespace. If another instance
   holds it, this launch sends `activate` over a named pipe (current user only, named per
   session) and exits; the running instance opens the dashboard.
2. **Host start.** Resolving the services opens the database (migrations, crash recovery) and
   creates the Windows sources. The session source is resolved on the main thread first, so
   `SystemEvents` delivers its events through the tray's message loop. `TrackingController`, a
   hosted service, starts the `TrackingService` run.
3. **Tray.** `TrayApplicationContext` shows a `NotifyIcon` with _Open dashboard_,
   _Pause tracking_ (for 30 minutes, an hour, until midnight or until resumed; _Resume tracking_
   while paused), _Start with Windows_, _Settings_ (#22), _Open log folder_ and _Quit_, and
   `Application.Run` pumps messages until Quit or the host stops. The tooltip shows today's
   active time, refreshed every 30 s. _Start with Windows_ is a value in the current user's
   `Run` key (`AutostartRegistration` in `Timeglass.Windows`), which also honours the
   _disabled_ flag Task Manager writes under `StartupApproved\Run`. It is not in
   `settings.json`: the check mark is read from the registry each time the menu opens, so it
   can't drift.
4. **Shutdown.** After the message loop ends, the host is stopped and disposed off the UI
   thread: tracking closes the open interval and flushes, and the repository drains its queue.
   On logoff or shutdown the same flush already happened in the `ShuttingDown` handler, since
   Windows may end the process before the message loop returns.

Pausing ends the current tracking run the same way (close and flush); resuming starts a new one,
so paused time is not stored. `TrackingController` resumes a timed pause by itself, checking the
wall clock at least every 30 s so the pause ends on time after sleep. A pause does not survive
a restart.
The embedded API (#27) will be another hosted service.

### Logging and crashes

Code logs through `Microsoft.Extensions.Logging` only. The host replaces the default providers
with a rolling file (Serilog's file sink, in `%LOCALAPPDATA%\Timeglass\logs`, daily files kept
seven days) and the debugger output. `TimeglassOptions.LogLevel` sets the file's level through
a level switch, so a configuration reload changes it without a restart. Framework categories
(`Microsoft.*`, `System.*`) are kept at Warning.

Levels: tracking state changes (interval opened or closed, with app and kind) at Debug,
checkpoints at Trace, recoverable failures at Warning or Error. **Window titles are never
logged**, at any level: log files end up attached to bug reports.

`CrashHandler` catches what nothing else did. A UI thread exception (`Application.ThreadException`)
is logged as Critical and the app quits the normal way, so the open interval is closed and
stored. An unobserved task exception is logged and marked observed. An exception on another
thread (`AppDomain.UnhandledException`) is logged, but the process ends regardless; the next
start closes the interval it left open. The file is written through on every entry, so the
last entry survives the crash.

## Threading model

```text
 hook thread (Win32 msg loop) ─┐
 SystemEvents thread ──────────┼─► Channel<TrackingEvent> ─► engine reader ─► Channel<writes> ─► SQLite writer
 TimeProvider timer (ticks) ───┘      (many writers)          (single task)      (many writers)    (single task)
                                                                                                       │
 Kestrel request threads ─────────────────────────────── own pooled connections, WAL reads ◄──────────┘
```

- **Ports may raise events on any thread.** Handlers do nothing but write to an unbounded
  `Channel` (`TrackingService`), so a slow consumer never blocks a Win32 callback.
- **One reader runs the engine.** Everything after the channel is sequential, so the engine
  needs no locks and its behaviour is deterministic for a given event order.
- **The foreground watcher owns a dedicated thread** that installs the hooks, runs the message
  loop Windows delivers the callbacks on, and runs the polling timer. Its change detection
  needs no locks either.
- **One writer touches the database for tracking.** `IntervalRepository.ApplyAsync` only
  queues; a background task writes whatever has queued up in one transaction. `FlushAsync`
  waits for everything queued so far. The tracker never waits on the disk.
- **Readers use their own connections.** SQLite runs in WAL mode, so API queries read while
  the writer writes.
- **Shutdown.** On `ShuttingDown`, the session handler blocks for up to 5 s while the open
  interval is closed and flushed, because Windows may end the process as soon as the handler
  returns. On normal exit, `RunAsync` drains the channel, closes the open interval and flushes.

## Testing

| Layer            | Project                                             | Runs on        |
| ---------------- | --------------------------------------------------- | -------------- |
| Engine and rules | `Timeglass.Core.Tests` (incl. property tests)       | Linux, Windows |
| SQLite           | `Timeglass.Storage.Tests` (real temp files)         | Linux, Windows |
| API              | `Timeglass.Api.Tests`                               | Linux, Windows |
| Win32 adapters   | `Timeglass.Windows.IntegrationTests` (real windows) | Windows only   |
| Host and wiring  | `Timeglass.App.Tests` (DI graph, single instance)   | Windows only   |
| Frontend         | `web/src/**/*.test.tsx` (Vitest, MSW)               | Linux          |

Shared fakes for the ports live in `tests/Timeglass.Testing`. Coverage thresholds:
Core 90 %, Storage 80 %, Api 80 % (lines), web 70 % (lines).
