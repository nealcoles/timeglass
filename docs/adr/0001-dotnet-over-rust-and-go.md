# 0001: .NET over Rust or Go

- **Status:** Accepted
- **Date:** 2026-11-02

## Context

Timeglass is a Windows tray app that needs Win32 hooks (`SetWinEventHook`,
`GetLastInputInfo`, session and power notifications), a tray icon, SQLite, and a small HTTP
server. It is a personal project, so the deciding factors are how quickly we can build it,
how pleasant it is to test, and how small the native interop surface is.

Options considered: .NET (C#), Rust, Go.

## Decision

Use .NET 10 and C#.

- **Windows integration is first class.** `Microsoft.Windows.CsWin32` generates typed P/Invoke
  for exactly the APIs listed in `NativeMethods.txt`. WinForms gives a tray icon and a message
  loop with no extra dependencies. `Microsoft.Win32.SystemEvents` covers lock and power events.
- **One language for engine, storage and API.** ASP.NET Core minimal APIs and
  `Microsoft.Data.Sqlite` are in the box and well supported.
- **Testability.** `TimeProvider` and `FakeTimeProvider` make time-based logic deterministic;
  xUnit, Shouldly and coverlet are mature. Core, Storage and Api target plain `net10.0`, so most
  tests run on Linux CI.
- **Familiarity.** We are fastest in C#.

## Consequences

- The runtime is larger than a Rust or Go binary. A self-contained publish is tens of MB;
  acceptable for a desktop app, and framework-dependent builds stay small.
- Memory use at idle is higher than Rust would be. The tracker does very little work per
  second, so this is not expected to matter; revisit if it does.
- Rust would give stronger guarantees around the unsafe interop code. We keep that surface
  small and isolated in `Timeglass.Windows`, with integration tests on a real Windows runner.
- Go's Windows GUI and COM story is weaker; we would have needed more hand-written syscalls.
