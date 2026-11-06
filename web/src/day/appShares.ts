import type { AppTotalDto } from '../api/client.ts';
import { appColor, OTHER_COLOR } from './appColor.ts';

/** One row of the app share list. */
export interface AppShare {
  /** The app key, or `null` for the "Other" row. */
  key: string | null;
  name: string;
  durationMs: number;
  /** Whole percentage of the active time. */
  percent: number;
  /** Exact fraction of the active time, for the bar width. */
  share: number;
  color: string;
  /** How many apps the "Other" row stands for; 1 for a single app. */
  count: number;
}

/** How many apps the day view lists by name before folding the rest into "Other". */
export const TOP_APPS = 6;

/**
 * The top `top` apps by time, longest first, and one "Other" row for the rest. A single leftover
 * app is shown by name, since an "Other" row for one app hides its name for nothing.
 */
export function appShares(apps: readonly AppTotalDto[], top: number = TOP_APPS): AppShare[] {
  const sorted = [...apps].sort((a, b) => b.durationMs - a.durationMs);
  const named = sorted.length <= top + 1 ? sorted : sorted.slice(0, top);
  const rest = sorted.slice(named.length);

  const rows: AppShare[] = named.map((a) => ({
    key: a.app.key,
    name: a.app.name,
    durationMs: a.durationMs,
    percent: a.percent,
    share: a.share,
    color: appColor(a.app.key),
    count: 1,
  }));

  if (rest.length > 0) {
    rows.push({
      key: null,
      name: 'Other',
      durationMs: rest.reduce((sum, a) => sum + a.durationMs, 0),
      // The API's percentages add up to 100, so the sum keeps the list adding up to 100.
      percent: rest.reduce((sum, a) => sum + a.percent, 0),
      share: rest.reduce((sum, a) => sum + a.share, 0),
      color: OTHER_COLOR,
      count: rest.length,
    });
  }
  return rows;
}
