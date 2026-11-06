import { formatDuration } from '../format.ts';
import { Link } from '../Link.tsx';
import { paths } from '../router.ts';
import type { AppShare } from './appShares.ts';

interface AppShareListProps {
  rows: readonly AppShare[];
  /** The day the rows are for; each named app links to its titles on that day (#34). */
  date: string;
}

/**
 * Where the time went, as a bar list. It is a real table, so the chart and its table alternative
 * are the same element: a screen reader reads app, time and share per row, and the bars are only
 * decoration for sighted users. The longest app fills the bar track, so short apps stay visible.
 */
export function AppShareList({ rows, date }: AppShareListProps) {
  const longest = Math.max(...rows.map((r) => r.durationMs), 1);

  return (
    <table className="app-shares">
      <caption className="visually-hidden">Active time per app, longest first</caption>
      <thead className="visually-hidden">
        <tr>
          <th scope="col">App</th>
          <th scope="col">Time</th>
          <th scope="col">Share</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr key={row.key ?? 'other'} data-app={row.key ?? 'other'}>
            <th scope="row" className="app-shares__name">
              <span
                className="app-shares__swatch"
                style={{ backgroundColor: row.color }}
                aria-hidden="true"
              />
              {row.key === null ? (
                row.name
              ) : (
                <Link href={paths.app(date, row.key)} className="app-shares__link">
                  {row.name}
                </Link>
              )}
              {row.key === null && (
                <span className="app-shares__count">
                  {' '}
                  ({row.count} {row.count === 1 ? 'app' : 'apps'})
                </span>
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
