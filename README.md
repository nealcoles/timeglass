# Timeglass

Timeglass is a personal activity tracker for Windows. It runs in the tray, records which
application (and optionally which window title) is in the foreground, notices when you are
away (idle, locked, asleep), and shows where your day went in a local web UI.

Everything stays on your machine: data goes into a local SQLite file, and the UI talks to a
small HTTP API that only listens on `127.0.0.1`.

> **Status:** early development. The tray app tracks and stores your day; the HTTP API and the
> real UI are still being built, so there is nothing to look at yet. See the
> [issues](https://github.com/sevenam/timeglass/issues) and milestones for what's next.
> Screenshots will follow once there is a UI worth showing.

## Repository layout

| Path                    | What it is                                                           |
| ----------------------- | -------------------------------------------------------------------- |
| `src/Timeglass.Core`    | Domain model, ports (interfaces) and the tracking engine. No I/O.    |
| `src/Timeglass.Storage` | SQLite schema, migrations and the interval repository.               |
| `src/Timeglass.Api`     | Local HTTP API (ASP.NET Core).                                       |
| `src/Timeglass.Windows` | Win32 adapters: foreground watcher, idle and session sources.        |
| `src/Timeglass.App`     | The tray app (`Timeglass.exe`): wires everything together.           |
| `tests/`                | Unit, storage, API and Windows integration tests, plus shared fakes. |
| `web/`                  | React + TypeScript frontend (Vite).                                  |
| `docs/`                 | [Architecture overview](docs/architecture.md) and [ADRs](docs/adr/). |

There are two solutions:

- `Timeglass.slnx` is everything. It needs Windows to build, because `Timeglass.Windows` and
  `Timeglass.App` target `net10.0-windows`.
- `Timeglass.Portable.slnx` is Core, Storage, Api and their tests. It builds and tests on any
  OS, and is what the Linux CI job runs.

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download) (the exact feature band is pinned in
  `global.json`, with `rollForward: latestFeature`).
- [Node.js 22+](https://nodejs.org/) with npm, for the frontend (`web/.nvmrc` has the
  version CI uses).
- Windows 10 1809 or later to build and run the full app. Linux and macOS can build and test
  the portable projects and the frontend.

## Build and test

Clone and enter the repository:

```sh
git clone https://github.com/sevenam/timeglass.git
cd timeglass
```

### .NET

On Windows, the full solution:

```sh
dotnet build Timeglass.slnx
dotnet test Timeglass.slnx
```

On Linux or macOS, the portable projects:

```sh
dotnet build Timeglass.Portable.slnx
dotnet test Timeglass.Portable.slnx
```

Warnings are errors, and code style is enforced at build time. Before pushing, check
formatting the way CI does:

```sh
dotnet format Timeglass.Portable.slnx --verify-no-changes
```

The Windows integration tests (`tests/Timeglass.Windows.IntegrationTests`) create real
top-level windows and bring them to the front, so they need an interactive Windows session
(the GitHub `windows-latest` runner has one) and only run as part of `Timeglass.slnx`. Don't
type into other windows while they run.

### Frontend

```sh
cd web
npm ci
npm run lint
npm run format:check
npm run typecheck
npm test
npm run build      # output in web/dist
```

`npm run test:coverage` runs the tests with the coverage threshold CI enforces.

[CONTRIBUTING.md](CONTRIBUTING.md) has the full list of checks CI runs, including coverage.

## Run

On Windows, run the tray app:

```sh
dotnet run --project src/Timeglass.App
```

It puts an hourglass in the notification area and tracks until you choose **Quit** from its
menu. Only one copy runs per user session; starting it again asks the running one to open the
dashboard. To keep test data out of your real database, pass another file:
`dotnet run --project src/Timeglass.App -- --Timeglass:DatabasePath=C:\temp\test.db`.

The dashboard isn't served yet (#27, #30). For frontend work, start the Vite dev server:

```sh
cd web
npm run dev
```

It proxies `/api` to the backend on `http://127.0.0.1:5381`. Set `TIMEGLASS_API_PORT` to use
another port. Until the API exists, the page shows the tracker as offline.

## Where data is stored

| What      | Where                                                  |
| --------- | ------------------------------------------------------ |
| Database  | `%LOCALAPPDATA%\Timeglass\timeglass.db` (SQLite)       |
| Log files | `%LOCALAPPDATA%\Timeglass\logs\timeglass-YYYYMMDD.log` |
| Settings  | `%LOCALAPPDATA%\Timeglass\settings.json`              |

The database uses WAL mode, so you will also see `timeglass.db-wal` and `timeglass.db-shm`
next to it while the app runs. Timestamps are stored as UTC Unix milliseconds. To start over,
quit the app and delete the folder.

Log files roll over daily (and past 10 MB) and are deleted after seven days. The tray menu's
_Open log folder_ opens them. The level is `Information` by default; set `"logLevel": "Debug"`
in the settings to also log tracking state changes. Window titles are never written to the log.

## Settings

`settings.json` is created with the defaults on first start. Edits apply while the app runs;
an edit with invalid values is logged and ignored (the previous settings stay), while invalid
settings at startup stop the app with a message that names the problem.

| Setting              | Default         | Meaning                                                                         |
| -------------------- | --------------- | ------------------------------------------------------------------------------- |
| `idleThreshold`      | `"00:05:00"`    | No input for this long counts as away (30 s to 4 h). `null` turns it off.       |
| `idleExemptApps`     | `[]`            | App keys that never go idle in front, e.g. `["vlc", "zoom"]`.                   |
| `minimumInterval`    | `"00:00:01"`    | Shorter visits to a window count towards the previous one (up to 1 min).        |
| `checkpointInterval` | `"00:00:15"`    | How often the open interval is saved, bounding loss on a crash (1 s to 10 min). |
| `recordWindowTitles` | `true`          | Store window titles. Applied by the privacy controls (#24).                     |
| `excludedApps`       | `[]`            | App keys that are not recorded. Applied by the privacy controls (#24).          |
| `appAliases`         | `[]`            | Renames and extra apps on top of the built-in aliases, see below.               |
| `apiPort`            | `5381`          | Port of the local API (#27), 1024 to 65535.                                     |
| `logLevel`           | `"Information"` | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical` or `None`.      |

An alias with the key of a built-in app renames it, or adds executables to it; any other key
adds an app. For example, to call Outlook "Mail" and count Cursor as VS Code:

```json
"appAliases": [
  { "key": "outlook", "name": "Mail" },
  { "key": "vscode", "exes": ["cursor.exe"] }
]
```

## Documentation

- [Architecture overview](docs/architecture.md): layers, dependency rule, data flow and
  threading model.
- [Architecture Decision Records](docs/adr/): why things are the way they are.
- [Contributing](CONTRIBUTING.md): branches, pull requests, commit messages and the full
  local test run.

## License

[MIT](LICENSE)
