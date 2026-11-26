import type { AppTotalDto } from '../api/client.ts';
import { appColors } from './appColor.ts';

/** One row of the app share list. */
export interface AppShare {
  key: string;
  name: string;
  durationMs: number;
  /** Whole percentage of the active time. */
  percent: number;
  /** Exact fraction of the active time, for the bar width. */
  share: number;
  color: string;
}

/** Every app by time, longest first, each with its own colour (#96). */
export function appShares(apps: readonly AppTotalDto[]): AppShare[] {
  const colorFor = appColors(apps.map((a) => a.app.key));
  return [...apps]
    .sort((a, b) => b.durationMs - a.durationMs)
    .map((a) => ({
      key: a.app.key,
      name: a.app.name,
      durationMs: a.durationMs,
      percent: a.percent,
      share: a.share,
      color: colorFor(a.app.key),
    }));
}
