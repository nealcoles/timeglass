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
  it('lists every app by name', () => {
    const rows = appShares([app('a', 30, 60), app('b', 20, 40)]);

    expect(rows.map((r) => r.name)).toEqual(['A', 'B']);
    expect(rows[0]?.color).toBe(appColor('a'));
  });

  it('sorts longest first even if the API did not', () => {
    const rows = appShares([app('a', 10, 20), app('b', 40, 80)]);

    expect(rows.map((r) => r.key)).toEqual(['b', 'a']);
  });

  it('resolves hash collisions independently of input order and duration', () => {
    expect(appColor('app-19')).toBe(appColor('app-24'));
    const first = appShares([app('app-19', 10, 50), app('app-24', 20, 50)]);
    const second = appShares([app('app-24', 10, 50), app('app-19', 20, 50)]);
    const colors = (rows: typeof first) =>
      Object.fromEntries(rows.map((row) => [row.key, row.color]));

    expect(colors(first)).toEqual(colors(second));
    expect(colors(first)['app-19']).not.toBe(colors(first)['app-24']);
  });

  it('spreads close colours apart and supports more than 360 keys', () => {
    const rows = appShares(['vscode', 'teams', 'terminal', 'edge'].map((key) => app(key, 1, 25)));
    const hues = rows.map((row) => Number(row.color.match(/^hsl\((\d+)/)?.[1]));
    for (let i = 0; i < hues.length; i++) {
      for (let j = i + 1; j < hues.length; j++) {
        const distance = Math.abs((hues[i] ?? 0) - (hues[j] ?? 0));
        expect(Math.min(distance, 360 - distance)).toBeGreaterThanOrEqual(24);
      }
    }

    const many = appShares(Array.from({ length: 400 }, (_, i) => app(`key-${i}`, 1, 1)));
    expect(new Set(many.map((row) => row.color)).size).toBe(400);
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
