import type { DayTotalDto } from '../api/client.ts';
import { formatDuration } from '../format.ts';

/** One coloured piece of a day's bar. */
export interface BarSegment {
  key: string;
  name: string;
  durationMs: number;
  /** Width as a fraction of the longest day in the week, so the bars compare across days. */
  width: number;
  color: string;
}

/**
 * A day's stacked bar: one segment per app, longest first.
 * `colorFor` is the week's app list colours, so each app is the same colour in the bar and the list.
 */
export function barSegments(
  day: DayTotalDto,
  longestMs: number,
  colorFor: (appKey: string) => string,
): BarSegment[] {
  const scale = longestMs > 0 ? longestMs : 1;
  return day.apps.map((a) => ({
    key: a.app.key,
    name: a.app.name,
    durationMs: a.durationMs,
    width: a.durationMs / scale,
    color: colorFor(a.app.key),
  }));
}

/** `Visual Studio Code 3h, Outlook 1h 45m`: the bar for a screen reader. */
export function describeSegments(segments: readonly BarSegment[]): string {
  return segments.map((s) => `${s.name} ${formatDuration(s.durationMs)}`).join(', ');
}
