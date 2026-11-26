import { formatDuration } from '../format.ts';
import { Link } from '../Link.tsx';
import { AWAY_GROUP_KEY } from '../groups/groupKeys.ts';
import { paths } from '../router.ts';
import type { AppShare } from './appShares.ts';

interface AppShareListProps {
  rows: readonly AppShare[];
  /**
   * The day the rows are for; each named app links to its titles on that day (#34). Without one
   * (the week view), the apps aren't links.
   */
  date?: string;
  grouped?: boolean;
  groupHref?: (name: string) => string;
  appHref?: (key: string) => string;
}

/**
 * Where the time went, as a bar list. It is a real table, so the chart and its table alternative
 * are the same element: a screen reader reads app, time and share per row, and the bars are only
 * decoration for sighted users. The longest app fills the bar track, so short apps stay visible.
 */
export function AppShareList({
  rows,
  date,
  grouped = false,
  groupHref,
  appHref,
}: AppShareListProps) {
  const longest = Math.max(...rows.map((r) => r.durationMs), 1);

  return (
    <table className="app-shares">
      <caption className="visually-hidden">
        {grouped ? 'Tracked time per category' : 'Active time per app'}, longest first
      </caption>
      <thead className="visually-hidden">
        <tr>
          <th scope="col">{grouped ? 'Category' : 'App'}</th>
          <th scope="col">Time</th>
          <th scope="col">Share</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr key={row.key} data-app={row.key}>
            <th scope="row" className="app-shares__name">
              <span
                className="app-shares__swatch"
                style={{ backgroundColor: row.color }}
                aria-hidden="true"
              />
              {groupHref && row.key !== AWAY_GROUP_KEY ? (
                <Link href={groupHref(row.name)} className="app-shares__link">
                  {row.name}
                </Link>
              ) : appHref ? (
                <Link href={appHref(row.key)} className="app-shares__link">
                  {row.name}
                </Link>
              ) : date === undefined ? (
                row.name
              ) : (
                <Link href={paths.app(date, row.key)} className="app-shares__link">
                  {row.name}
                </Link>
              )}
              <span className="app-shares__track" aria-hidden="true">
                <span
                  className="app-shares__bar"
                  style={{
                    width: `${(row.durationMs / longest) * 100}%`,
                    backgroundColor: row.color,
                  }}
                />
              </span>
            </th>
            <td className="app-shares__duration">{formatDuration(row.durationMs)}</td>
            <td className="app-shares__percent">{row.percent}%</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
