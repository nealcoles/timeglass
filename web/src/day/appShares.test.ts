import type { AppTotalDto } from '../api/client.ts';
import { appColor, OTHER_COLOR } from './appColor.ts';
import { appShares } from './appShares.ts';

const app = (key: string, minutes: number, percent: number): AppTotalDto => ({
  app: { key, name: key.toUpperCase() },
  durationMs: minutes * 60_000,
  share: percent / 100,
  percent,
});

describe('appColor', () => {
  it('is the same for the same key', () => {
    expect(appColor('vscode')).toBe(appColor('vscode'));
  });

  it('differs between apps', () => {
    expect(appColor('vscode')).not.toBe(appColor('outlook'));
  });

  it('is a valid hsl colour', () => {
    expect(appColor('anything')).toMatch(/^hsl\(\d{1,3} 65% 50%\)$/);
  });
});

describe('appShares', () => {
  it('lists every app by name when they fit', () => {
    const rows = appShares([app('a', 30, 60), app('b', 20, 40)], 2);

    expect(rows.map((r) => r.name)).toEqual(['A', 'B']);
    expect(rows[0]?.color).toBe(appColor('a'));
  });

  it('sorts longest first even if the API did not', () => {
    const rows = appShares([app('a', 10, 20), app('b', 40, 80)]);

    expect(rows.map((r) => r.key)).toEqual(['b', 'a']);
  });

  it('folds the rest into Other, keeping the total at 100%', () => {
    const rows = appShares(
      [app('a', 50, 50), app('b', 30, 30), app('c', 10, 10), app('d', 6, 6), app('e', 4, 4)],
      2,
    );

    expect(rows.map((r) => r.name)).toEqual(['A', 'B', 'Other']);
    const other = rows[2];
    expect(other).toBeDefined();
    expect(other).toMatchObject({ key: null, count: 3, percent: 20, color: OTHER_COLOR });
    expect(other?.durationMs).toBe(20 * 60_000);
    expect(rows.reduce((sum, r) => sum + r.percent, 0)).toBe(100);
  });

  it('shows a single leftover app by name instead of as Other', () => {
    const rows = appShares([app('a', 50, 50), app('b', 30, 30), app('c', 20, 20)], 2);

    expect(rows.map((r) => r.name)).toEqual(['A', 'B', 'C']);
  });

  it('returns nothing for a day without apps', () => {
    expect(appShares([])).toEqual([]);
  });
});
