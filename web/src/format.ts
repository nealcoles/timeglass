/**
 * Formatting for the dashboard. Times come from the API with the machine's own UTC offset
 * (`2026-11-05T08:12:31+01:00`), so the wall-clock time is read from the string itself rather than
 * converted to the browser's time zone; the two are the same machine, but this keeps tests stable.
 */

const MINUTE_MS = 60_000;

/** `7h 12m`, `45m`, `2h`, or `<1m` for anything shorter than a minute but not zero. */
export function formatDuration(ms: number): string {
  if (ms <= 0) {
    return '0m';
  }
  const totalMinutes = Math.floor(ms / MINUTE_MS);
  if (totalMinutes === 0) {
    return '<1m';
  }
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  if (hours === 0) {
    return `${minutes}m`;
  }
  return minutes === 0 ? `${hours}h` : `${hours}h ${minutes}m`;
}

/** `08:12` from an ISO date-time with an offset, as the clock on the machine showed it. */
export function formatClockTime(isoDateTime: string): string {
  const match = /T(\d{2}:\d{2})/.exec(isoDateTime);
  return match?.[1] ?? isoDateTime;
}

/** `Thursday, 5 November 2026` (in the browser's locale) from `2026-11-05`. */
export function formatLongDate(date: string, locale?: string): string {
  return new Intl.DateTimeFormat(locale, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(`${date}T00:00:00Z`));
}

/** Today's date in the browser's time zone, as `yyyy-MM-dd`. */
export function localToday(now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}
