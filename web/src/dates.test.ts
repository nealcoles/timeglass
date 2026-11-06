import {
  addDays,
  formatDateRange,
  formatShortDay,
  isValidDate,
  weekDates,
  weekStart,
} from './dates.ts';

describe('dates', () => {
  it.each([
    ['2026-11-05', true],
    ['2028-02-29', true],
    ['2026-02-29', false],
    ['2026-02-30', false],
    ['2026-13-01', false],
    ['5-11-2026', false],
    ['', false],
  ])('knows whether %s is a date', (value, valid) => {
    expect(isValidDate(value)).toBe(valid);
  });

  it('adds days across months, years and daylight saving changes', () => {
    expect(addDays('2026-11-05', 1)).toBe('2026-11-06');
    expect(addDays('2026-11-01', -1)).toBe('2026-10-31');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(addDays('2026-03-28', 2)).toBe('2026-03-30');
    expect(addDays('2026-10-24', 7)).toBe('2026-10-31');
  });

  it.each([
    ['2026-11-02', '2026-11-02'],
    ['2026-11-05', '2026-11-02'],
    ['2026-11-08', '2026-11-02'],
    ['2026-11-09', '2026-11-09'],
    ['2027-01-01', '2026-12-28'],
  ])('starts the week of %s on Monday %s', (date, monday) => {
    expect(weekStart(date)).toBe(monday);
  });

  it('lists the seven days of a week', () => {
    expect(weekDates('2026-12-28')).toEqual([
      '2026-12-28',
      '2026-12-29',
      '2026-12-30',
      '2026-12-31',
      '2027-01-01',
      '2027-01-02',
      '2027-01-03',
    ]);
  });

  it('formats short days and ranges', () => {
    expect(formatShortDay('2026-11-02', 'en-GB')).toBe('Mon 2 Nov');
    expect(formatDateRange('2026-11-02', '2026-11-08', 'en-GB')).toMatch(
      /^2\s*–\s*8 November 2026$/,
    );
    expect(formatDateRange('2026-12-28', '2027-01-03', 'en-GB')).toMatch(
      /^28 December 2026\s*–\s*3 January 2027$/,
    );
  });
});
