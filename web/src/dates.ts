/**
 * Calendar arithmetic on local dates (`yyyy-MM-dd`). The dates are the tracker's own calendar
 * days, so they are handled as plain dates in UTC: no time zone or daylight saving shift can move
 * them.
 */

const DATE = /^\d{4}-\d{2}-\d{2}$/;

const toUtc = (date: string) => new Date(`${date}T00:00:00Z`);
const fromUtc = (d: Date) => d.toISOString().slice(0, 10);

/** True for a real calendar date such as `2026-11-05`; false for `2026-02-30` or `5-11-2026`. */
export function isValidDate(value: string): boolean {
  return (
    DATE.test(value) && !Number.isNaN(toUtc(value).getTime()) && fromUtc(toUtc(value)) === value
  );
}

/** The date `days` days after `date` (before it, for a negative number). */
export function addDays(date: string, days: number): string {
  const d = toUtc(date);
  d.setUTCDate(d.getUTCDate() + days);
  return fromUtc(d);
}

/** The Monday of the (ISO) week that `date` is in. */
export function weekStart(date: string): string {
  const weekday = (toUtc(date).getUTCDay() + 6) % 7; // Monday = 0
  return addDays(date, -weekday);
}

/** The seven dates of the week starting on `monday`. */
export function weekDates(monday: string): string[] {
  return Array.from({ length: 7 }, (_, i) => addDays(monday, i));
}

/** `Mon 2 Nov` (in the browser's locale) from `2026-11-02`. */
export function formatShortDay(date: string, locale?: string): string {
  return new Intl.DateTimeFormat(locale, {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    timeZone: 'UTC',
  }).format(toUtc(date));
}

/** `2 – 8 November 2026` (in the browser's locale) for the dates from `from` to `to`. */
export function formatDateRange(from: string, to: string, locale?: string): string {
  return new Intl.DateTimeFormat(locale, {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  }).formatRange(toUtc(from), toUtc(to));
}

/** `2 Nov` (in the browser's locale) from `2026-11-02`. */
export function formatDayMonth(date: string, locale?: string): string {
  return new Intl.DateTimeFormat(locale, {
    day: 'numeric',
    month: 'short',
    timeZone: 'UTC',
  }).format(new Date(`${date}T00:00:00Z`));
}
