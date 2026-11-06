import { formatClockTime, formatDuration, formatLongDate, localToday } from './format.ts';

describe('formatDuration', () => {
  it.each([
    [0, '0m'],
    [-5, '0m'],
    [59_000, '<1m'],
    [60_000, '1m'],
    [45 * 60_000, '45m'],
    [120 * 60_000, '2h'],
    [432 * 60_000 + 30_000, '7h 12m'],
  ])('formats %d ms as %s', (ms, expected) => {
    expect(formatDuration(ms)).toBe(expected);
  });
});

describe('formatClockTime', () => {
  it('reads the wall-clock time from the string, whatever the offset', () => {
    expect(formatClockTime('2026-11-05T08:12:31+01:00')).toBe('08:12');
    expect(formatClockTime('2026-11-05T23:59:00-08:00')).toBe('23:59');
  });

  it('returns the input when it has no time', () => {
    expect(formatClockTime('2026-11-05')).toBe('2026-11-05');
  });
});

describe('formatLongDate', () => {
  it('names the weekday of the date itself, not of the browser time zone', () => {
    expect(formatLongDate('2026-11-05', 'en-GB')).toMatch(/^Thursday,? 5 November 2026$/);
  });
});

describe('localToday', () => {
  it('formats the local date as yyyy-MM-dd', () => {
    expect(localToday(new Date(2026, 0, 7, 23, 30))).toBe('2026-01-07');
  });
});
