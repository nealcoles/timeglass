import { useDaySummary } from '../api/queries.ts';
import { errorMessage } from '../api/errors.ts';
import { formatClockTime, formatDuration, formatLongDate } from '../format.ts';
import { AppShareList } from './AppShareList.tsx';
import { OTHER_COLOR } from './appColor.ts';
import { appShares } from './appShares.ts';
import { DayTimeline } from './DayTimeline.tsx';

interface DayViewProps {
  /** The local date to show, `yyyy-MM-dd`. */
  date: string;
  /** True when `date` is today, which only changes the wording. */
  isToday?: boolean;
}

/**
 * The main screen (#32, #33): how long you were active on a day, when, and which apps the time
 * went to.
 */
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

          <DaySections date={date} rows={appShares(summary.data.apps)} />
        </>
      )}
    </section>
  );
}

function DaySections({ date, rows }: { date: string; rows: ReturnType<typeof appShares> }) {
  // The timeline uses the list's colours, so an app folded into "Other" is grey in both.
  const colors = new Map(rows.flatMap((r) => (r.key === null ? [] : [[r.key, r.color] as const])));
  const colorFor = (appKey: string) => colors.get(appKey) ?? OTHER_COLOR;

  return (
    <>
      <h3>When</h3>
      <DayTimeline date={date} colorFor={colorFor} />

      <h3>Where the time went</h3>
      <AppShareList rows={rows} />
    </>
  );
}
