# 0005: Paused and excluded time is a gap, not an interval

- **Status:** Accepted
- **Date:** 2026-11-05

## Context

Window titles contain email subjects and document names, so the user needs to control what is
recorded (#24): pause tracking, exclude apps, stop recording titles, and delete what has been
recorded. For paused time, and for time spent in an app the user doesn't want tracked, we had
to decide whether to store something (a `Paused` or `Excluded` interval kind) or nothing.

## Decision

**Store nothing.** Paused time and time in an excluded app are gaps between intervals.

- **Pause** stops the tracking run, which closes the open interval, and resuming starts a new
  run. The engine has no paused state. Timed pauses (30 min, 1 h, until local midnight) are a
  timer in the host that checks the wall clock, so they end on time after sleep. A pause is not
  remembered across a restart of the app.
- **Excluded apps** (`excludedApps`): while one is in front, the engine has no open interval.
  The minimum-interval rule applies to switching into and out of it, so Alt-Tab flicker through
  an excluded app changes nothing. Idle detection is off there (there is nothing to end); lock
  and sleep still record `Away`.
- **Apps without titles** (`appsWithoutTitles`) and `recordWindowTitles: false`: time is
  tracked, the title is never put into the interval. Turning one of these on while an interval
  is open removes the title from that interval too.
- **Delete range** cuts intervals at the range edges and removes what is inside, with SQLite's
  `secure_delete` so the text does not stay in free pages. While tracking, the open interval
  is closed first if the range reaches it, so the tracker can't write deleted time back.

## Consequences

- The privacy promise is easy to state and check: what the user excluded is not in the file.
  Tests assert this directly against the database.
- Reports can't tell "paused" or "excluded" apart from "the app wasn't running". If the UI
  wants to show paused stretches, it needs a separate record; nothing here prevents adding one.
- Intervals are no longer contiguous across a pause or an excluded app, only within one run of
  tracked time. Nothing relied on contiguity across runs (a restart already made a gap).
- The `intervals.kind` column keeps its two values; no migration.
