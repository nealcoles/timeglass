import { appDay } from '../test/fixtures.ts';
import { filterTitles, hasNoTitles, type TitleRow } from './titleRows.ts';

const row = (title: string | null, minutes: number): TitleRow => ({
  title,
  durationMs: minutes * 60_000,
  share: 0,
  percent: 0,
});

describe('filterTitles', () => {
  const titles = [row('Inbox - Outlook', 30), row(null, 20), row('Budget.xlsx - Excel', 10)];

  it('keeps every title, the untitled one too, while the search is empty', () => {
    expect(filterTitles(titles, '  ')).toEqual(titles);
  });

  it('matches anywhere in the title, ignoring case, and leaves out untitled time', () => {
    expect(filterTitles(titles, 'BUDGET').map((t) => t.title)).toEqual(['Budget.xlsx - Excel']);
    expect(filterTitles(titles, ' - ').map((t) => t.title)).toEqual([
      'Inbox - Outlook',
      'Budget.xlsx - Excel',
    ]);
    expect(filterTitles(titles, 'nothing')).toEqual([]);
  });
});

describe('hasNoTitles', () => {
  it('is true when all of the time is untitled', () => {
    expect(hasNoTitles({ ...appDay(), titles: [{ ...row(null, 180), percent: 100 }] })).toBe(true);
  });

  it('is false when some time has a title, or there is no time', () => {
    expect(hasNoTitles(appDay())).toBe(false);
    expect(hasNoTitles(appDay(undefined, 'outlook'))).toBe(false);
  });
});
