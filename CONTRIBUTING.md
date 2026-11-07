# Contributing

Thanks for helping. This page covers how changes get in and how to check them locally before
you push. For how the code is organised, read [docs/architecture.md](docs/architecture.md)
first.

## Branches and pull requests

- `main` should always build and pass its tests. Every change goes through a pull request
  with green CI; don't push to `main` directly.
- Work on a short-lived branch from the latest `main`, named after the issue:
  `feat/15-foreground-watcher`, `fix/42-dst-midnight`, `docs/7-developer-docs`.
- One issue per pull request. Link it in the description with `Closes #N` so it closes on
  merge.
- Fill in the [pull request template](.github/pull_request_template.md): what changed, tests
  added, docs updated.
- Keep the branch up to date with `main` by rebasing or merging; either is fine.
- Pull requests are merged with a merge commit, so the history shows one merge per issue.
- If a change alters behaviour or architecture, update the docs in the same pull request. A
  new significant decision gets an [ADR](docs/adr/).

## Commit messages

- Imperative mood, capitalised, no trailing period, about 72 characters or less:
  `Add the Win32 foreground window watcher`.
- Say what the change does, not how you got there. Put the why in the body when it isn't
  obvious, wrapped at 72 characters.
- For changes that are only about tooling, use a lowercase prefix: `ci:` for workflows and
  build scripts, `deps:` for dependency bumps (Dependabot uses this), `docs:` for
  documentation-only changes.
- Reference the issue in the body when useful (`Part of #25`), and leave `Closes #N` to the
  pull request description.
- Small, focused commits are preferred, but there is no need to squash a branch before merge.

## Code style

- `.editorconfig` defines formatting for every language. Your editor should pick it up.
- .NET builds with warnings as errors and analyzers at the latest level. Code style is checked
  at build time and by `dotnet format`.
- Core must not read the clock directly; use `TimeProvider` (enforced by
  `src/Timeglass.Core/BannedSymbols.txt`).
- The frontend uses ESLint and Prettier (`npm run lint`, `npm run format`).

## Running the full test suite locally

This is what CI runs (see `.github/workflows/ci.yml`). Prerequisites are in the
[README](README.md#prerequisites).

### .NET on Windows (everything)

```sh
dotnet restore Timeglass.slnx
dotnet format Timeglass.slnx --verify-no-changes --no-restore
dotnet build Timeglass.slnx -c Release --no-restore
dotnet test Timeglass.slnx -c Release --no-build
```

The Release build of `Timeglass.App` runs `npm ci` and `npm run build` in `web/` and embeds
the dashboard, so it needs Node.js. If `web/dist` is already built, add
`-p:TimeglassBuildWeb=false` to skip npm, as CI does with the `web` job's artifact.

The Windows integration tests bring their own windows to the front. Leave the keyboard and
mouse alone while they run, or they may fail. CI runs them as a separate step; to do the same:

```sh
dotnet test Timeglass.slnx -c Release --no-build --filter "Category!=WindowsIntegration"
dotnet test Timeglass.slnx -c Release --no-build --filter "Category=WindowsIntegration"
```

A test that calls real Win32 APIs is tagged `[Trait("Category", Desktop.Category)]` and has
`Timeout = Desktop.TestTimeout`, so a hung message loop fails that test instead of stalling the
run. Tests that change the foreground window also join `[Collection(ForegroundCollection.Name)]`
so they run one at a time. The UWP test opens Calculator and is skipped where it isn't
installed (as on Windows Server images).

### .NET on Linux or macOS (portable projects)

```sh
dotnet restore Timeglass.Portable.slnx
dotnet format Timeglass.Portable.slnx --verify-no-changes --no-restore
dotnet build Timeglass.Portable.slnx -c Release --no-restore
dotnet test Timeglass.Portable.slnx -c Release --no-build \
  --results-directory TestResults \
  --collect "XPlat Code Coverage" --settings coverage.runsettings
```

### .NET coverage gates

After the test run with coverage above (on any OS; the commands are for bash, in PowerShell
replace the `\` continuations with backticks), merge the reports and check the
thresholds (Core 90 %, Storage 80 %, Api 80 % line coverage):

```sh
dotnet tool restore
dotnet reportgenerator "-reports:TestResults/**/coverage.cobertura.xml" \
  "-targetdir:coverage" "-reporttypes:Cobertura;Html"
python3 build/check-coverage.py coverage/Cobertura.xml \
  Timeglass.Core=90 Timeglass.Storage=80 Timeglass.Api=80
```

Open `coverage/index.html` to see what isn't covered. Delete `TestResults/` before a rerun so
old reports aren't merged in.

### Frontend

```sh
cd web
npm ci
npm run lint
npm run format:check
npm run check:api       # generated API types match openapi.json
npm run typecheck
npm run test:coverage   # fails below the line threshold in vite.config.ts
npm run build
```

`npm run format` fixes formatting; `npm run test:watch` reruns tests as you edit.

### End-to-end tests

The Playwright tests in `web/e2e` run the built dashboard against the dev host, so they need
the .NET SDK as well as Node.js. Playwright starts the dev host itself (with the clock stopped
and the time zone set, so the demo data never changes), or reuses one already on port 5410:

```sh
cd web
npx playwright install chromium   # once
npm run build
npm run e2e
```

A failed test keeps a trace in `web/test-results/`; open it with
`npx playwright show-trace web/test-results/<test>/trace.zip`. In CI the `e2e` job uploads
`test-results` and the HTML report as the `playwright-report` artifact when a test fails. Set
`PLAYWRIGHT_CHROMIUM_EXECUTABLE` to use a Chromium you already have.


### Changing the API

`src/Timeglass.Api/openapi.json` is generated from the endpoints and checked by a test. After
changing an endpoint or a response record, regenerate it and commit the result:

```sh
TIMEGLASS_UPDATE_OPENAPI=1 dotnet test tests/Timeglass.Api.Tests
```

(PowerShell: `$env:TIMEGLASS_UPDATE_OPENAPI=1; dotnet test tests/Timeglass.Api.Tests`.)

The dashboard's API types in `web/src/api/schema.d.ts` are generated from that file. Regenerate
them too and commit both; CI fails if they are out of date:

```sh
cd web
npm run generate:api
```

## Tests

- Logic goes in Core and gets unit tests in `Timeglass.Core.Tests`. Use the fakes in
  `tests/Timeglass.Testing` and `FakeTimeProvider` rather than real time or real windows.
- Storage tests use real SQLite files in a temp folder (`TempDatabase`).
- Anything that calls Win32 gets an integration test in `Timeglass.Windows.IntegrationTests`.
- API endpoints get tests in `Timeglass.Api.Tests`.
- The tray host and its wiring get tests in `Timeglass.App.Tests` (Windows only). A new service
  registered in `TimeglassHost` is covered by the composition test there.
- Frontend components get Vitest + Testing Library tests next to the component, with MSW for
  API calls. Every test starts with handlers that serve demo data (`web/src/test/handlers.ts`);
  override one with `server.use(...)`. Render with `renderWithApi` or `renderApiHook` from
  `web/src/test/render.tsx` so components get the API client and a fresh query cache.
- Never skip or disable a failing test to get CI green. Fix the code or the test.
