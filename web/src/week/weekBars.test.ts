import type { DayTotalDto } from '../api/client.ts';
import { OTHER_COLOR } from '../day/appColor.ts';
import { rangeSummary } from '../test/fixtures.ts';
import { barSegments, describeSegments } from './weekBars.ts';

const colorFor = (key: string) => (key === 'vscode' ? 'blue' : OTHER_COLOR);

function workday(): DayTotalDto {
  const day = rangeSummary('2026-11-05', '2026-11-05').days[0];
  if (!day) {
    throw new Error('no day');
  }
  return day;
}

describe('barSegments', () => {
  it('stacks every app of the day, scaled to the longest day', () => {
    const segments = barSegments(workday(), 840 * 60_000, colorFor);

    expect(segments.map((s) => [s.key, s.durationMs / 60_000, s.width, s.color])).toEqual([
      ['vscode', 180, 180 / 840, 'blue'],
      ['outlook', 105, 105 / 840, OTHER_COLOR],
      ['edge', 75, 75 / 840, OTHER_COLOR],
      ['teams', 45, 45 / 840, OTHER_COLOR],
      ['terminal', 15, 15 / 840, OTHER_COLOR],
    ]);
  });

  it('fills the bar of the longest day', () => {
    const day = workday();

    const total = barSegments(day, day.activeMs, colorFor).reduce((sum, s) => sum + s.width, 0);
    expect(total).toBeCloseTo(1);
  });

  it('is empty for a day without time', () => {
    const day = { ...workday(), activeMs: 0, apps: [] };

    expect(barSegments(day, 0, colorFor)).toEqual([]);
  });

  it('describes the bar in words', () => {
    expect(describeSegments(barSegments(workday(), 1, colorFor))).toBe(
      'Visual Studio Code 3h, Outlook 1h 45m, Microsoft Edge 1h 15m, Microsoft Teams 45m, Windows Terminal 15m',
    );
  });
});
