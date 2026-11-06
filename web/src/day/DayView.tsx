import { useDaySummary } from '../api/queries.ts';
import { errorMessage } from '../api/errors.ts';
import { formatClockTime, formatDuration, formatLongDate } from '../format.ts';
import { AppShareList } from './AppShareList.tsx';
import { appShares } from './appShares.ts';

interface DayViewProps {
  /** The local date to show, `yyyy-MM-dd`. */
  date: string;
  /** True when `date` is today, which only changes the wording. */
  isToday?: boolean;
}

/** The main screen (#32): how long you were active on a day and which apps the time went to. */
export function DayView({ date, isToday = false }: DayViewProps) {
  const summary = useDaySummary(date);
  const heading = isToday ? `Today, ${formatLongDate(date)}` : formatLongDate(date);

  return (
    <section className="day" aria-labelledby="day-heading" aria-busy={summary.isPending}>
      <h2 id="day-heading">{heading}</h2>

      {summary.isPending ? (
        <p role="status" className="day__loading">
          Loading the day…
        </p>
      ) : summary.isError ? (
        <p role="alert" className="day__error">
          {errorMessage(summary.error)}
        </p>
      ) : summary.data.activeMs === 0 && summary.data.apps.length === 0 ? (
        <p className="day__empty">
          {isToday
            ? 'Nothing tracked yet today. Time shows up here as you use your computer.'
            : 'Nothing was tracked on this day.'}
        </p>
      ) : (
        <>
          <dl className="day__totals">
            <div>
              <dt>Active</dt>
              <dd>{formatDuration(summary.data.activeMs)}</dd>
            </div>
            <div>
              <dt>Away</dt>
              <dd>{formatDuration(summary.data.awayMs)}</dd>
            </div>
            {summary.data.firstActivity && (
              <div>
                <dt>First activity</dt>
                <dd>{formatClockTime(summary.data.firstActivity)}</dd>
              </div>
            )}
            {summary.data.lastActivity && (
              <div>
                <dt>Last activity</dt>
                <dd>{formatClockTime(summary.data.lastActivity)}</dd>
              </div>
            )}
          </dl>

          <h3>Where the time went</h3>
          <AppShareList rows={appShares(summary.data.apps)} />
        </>
      )}
    </section>
  );
}
