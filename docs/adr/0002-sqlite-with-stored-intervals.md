# 0002: SQLite with stored intervals, not samples

- **Status:** Accepted
- **Date:** 2026-11-02

## Context

We need to record what was in the foreground over time and answer questions like "how long
in each app today", "what did the day look like", and "which window titles in this app".

Two common ways to record activity:

1. **Samples:** every N seconds, write a row with whatever is in front. Simple to write, but
   accuracy is bounded by N, storage grows with time rather than with activity, and every
   query has to re-derive durations and deal with gaps.
2. **Intervals:** write one row per stretch of time in one window, with a start and an end.

And where to put them: a local embedded database, flat files (JSON lines, CSV), or a server
database.

## Decision

Store **intervals** in a local **SQLite** file (`%LOCALAPPDATA%\Timeglass\timeglass.db`).

- The tracking engine produces non-overlapping, contiguous intervals with exact boundaries
  from OS events (backdated for flicker and idle), so durations are precise to the
  millisecond, not to the sample rate.
- One row per switch keeps the database small: a busy day is hundreds to a few thousand
  rows, not 86 400.
- Reporting queries are range scans plus `SUM(end - start)`, which SQLite does well with an
  index on `start_utc`.
- SQLite is a single file, needs no service, supports transactions and WAL (concurrent reads
  while the tracker writes), and is easy to back up or inspect.

The interval that is still running is stored too, with `is_open = 1` and its end moved
forward on a checkpoint every 15 s. After a crash it is closed at its last checkpoint on the
next start, so at most one checkpoint interval is lost.

## Consequences

- The engine is more involved than a sampler: it owns the rules for flicker, idle, lock and
  clock jumps. In return those rules live in one pure, heavily tested class instead of in
  every query.
- Reports that split time by local day or hour must clip intervals at the boundaries (#25).
- Changing what we record (e.g. adding a field) changes intervals going forward only; old rows
  can't be re-sampled.
- Timestamps are UTC Unix milliseconds. Local time, DST and midnight splits are a query-time
  concern.
- Schema changes go through numbered, embedded migration scripts.
