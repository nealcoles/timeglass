import type { DayTotalDto } from '../api/client.ts';
import { OTHER_COLOR } from '../day/appColor.ts';
import { formatDuration } from '../format.ts';

/** One coloured piece of a day's bar. */
export interface BarSegment {
  /** The app key, or `null` for the time in the day's other apps. */
  key: string | null;
  name: string;
  durationMs: number;
  /** Width as a fraction of the longest day in the week, so the bars compare across days. */
  width: number;
  color: string;
}

/**
 * A day's stacked bar: one segment per top app, then one for the rest of the day's active time.
 * `colorFor` is the week's app list colours, so each app is the same colour in the bar and the list.
 */
export function barSegments(
  day: DayTotalDto,
  longestMs: number,
  colorFor: (appKey: string) => string,
): BarSegment[] {
  const scale = longestMs > 0 ? longestMs : 1;
  const segments: BarSegment[] = day.topApps.map((a) => ({
    key: a.app.key,
    name: a.app.name,
    durationMs: a.durationMs,
    width: a.durationMs / scale,
    color: colorFor(a.app.key),
  }));
  const rest = day.activeMs - day.topApps.reduce((sum, a) => sum + a.durationMs, 0);
  if (rest > 0) {
    segments.push({
      key: null,
      name: 'Other apps',
      durationMs: rest,
      width: rest / scale,
      color: OTHER_COLOR,
    });
  }
  return segments;
}

/** `Visual Studio Code 3h, Outlook 1h 45m, other apps 30m`: the bar for a screen reader. */
export function describeSegments(segments: readonly BarSegment[]): string {
  return segments
    .map((s) => `${s.key === null ? 'other apps' : s.name} ${formatDuration(s.durationMs)}`)
    .join(', ');
}
