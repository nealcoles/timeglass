import type { AppTotalDto } from '../api/client.ts';
import { OTHER_COLOR } from './appColor.ts';
import { appShares } from './appShares.ts';

const app = (key: string, minutes: number, percent: number): AppTotalDto => ({
  app: { key, name: key.toUpperCase() },
  durationMs: minutes * 60_000,
  share: percent / 100,
  percent,
  color: `color-${key}`,
  lastActivity: '2026-11-05T12:00:00Z',
});

describe('appShares', () => {
  it('lists every app by name', () => {
    const rows = appShares([app('a', 30, 60), app('b', 20, 40)]);

    expect(rows.map((r) => r.name)).toEqual(['A', 'B']);
    expect(rows[0]?.color).toBe('color-a');
  });

  it('sorts longest first even if the API did not', () => {
    const rows = appShares([app('a', 10, 20), app('b', 40, 80)]);

    expect(rows.map((r) => r.key)).toEqual(['b', 'a']);
  });

  it('never folds apps into Other, however many there are (#96)', () => {
    const apps = 'abcdefghij'.split('').map((key, i) => app(key, 10 - i, 10));
    const rows = appShares(apps);

    expect(rows.map((r) => r.key)).toEqual('abcdefghij'.split(''));
    expect(rows.every((r) => r.color !== OTHER_COLOR)).toBe(true);
    expect(rows.reduce((sum, r) => sum + r.percent, 0)).toBe(100);
  });

  it('returns nothing for a day without apps', () => {
    expect(appShares([])).toEqual([]);
  });
});
