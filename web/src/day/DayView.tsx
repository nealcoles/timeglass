import { useState } from 'react';
import { useDaySummary, useGroupedRangeSummary } from '../api/queries.ts';
import { errorMessage } from '../api/errors.ts';
import { formatClockTime, formatDuration, formatLongDate, localToday } from '../format.ts';
import { DateNav } from '../nav/DateNav.tsx';
import { ViewSelector, type GroupingView } from '../groups/ViewSelector.tsx';
import { paths } from '../router.ts';
import { AppShareList } from './AppShareList.tsx';
import { OTHER_COLOR } from './appColor.ts';
import { appShares } from './appShares.ts';
import { DayTimeline } from './DayTimeline.tsx';

interface DayViewProps {
  /** The local date to show, `yyyy-MM-dd`. */
  date: string;
  /** Today's date, `yyyy-MM-dd`: the wording says "today", and the way forward stops there. */
  today?: string;
  initialView?: GroupingView;
}

/**
 * The main screen (#32, #33): how long you were active on a day, when, and which apps the time
 * went to. Each app links to its drill-down by window title (#34); the date bar moves between days
 * and to the week (#35).
 */
export function DayView({ date, today = localToday(), initialView = 'apps' }: DayViewProps) {
  const isToday = date === today;
  const summary = useDaySummary(date);
  const [view, setView] = useState<GroupingView>(initialView);
  const grouped = useGroupedRangeSummary(date, date, { enabled: view === 'groups' });
  const colors = new Map(
    (summary.data ? appShares(summary.data.apps) : []).map((row) => [row.key, row.color]),
  );
  const heading = isToday ? `Today, ${formatLongDate(date)}` : formatLongDate(date);

  return (
    <section className="day" aria-labelledby="day-heading" aria-busy={summary.isPending}>
      <h2 id="day-heading">{heading}</h2>
      <DateNav view="day" date={date} today={today} />

      {summary.isPending ? (
        <p role="status" className="day__loading">
          Loading the day…
        </p>
      ) : summary.isError ? (
        <p role="alert" className="day__error">
          {errorMessage(summary.error)}
        </p>
      ) : summary.data.activeMs === 0 &&
        summary.data.apps.length === 0 &&
        (view !== 'groups' || summary.data.awayMs === 0) ? (
        <>
          <ViewSelector value={view} onChange={setView} />
          <p className="day__empty">
            {summary.data.awayMs > 0
              ? 'No active time was tracked. Select Keyword group to see away time.'
              : isToday
                ? 'Nothing tracked yet today. Time shows up here as you use your computer.'
                : 'Nothing was tracked on this day.'}
          </p>
        </>
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
            {summary.data.firstActivity && (
              <div title="Time from your first to your last activity that falls inside your work hours, set in Settings">
                <dt>Workday</dt>
                <dd>{formatDuration(summary.data.workdayMs)}</dd>
              </div>
            )}
          </dl>

          <h3>When</h3>
          {view === 'groups' && (
            <p className="settings__hint">The timeline is coloured by application.</p>
          )}
          <DayTimeline
            date={date}
            colorFor={(key) => colors.get(key) ?? OTHER_COLOR}
            grouped={view === 'groups'}
          />
          <h3>Where the time went</h3>
          <ViewSelector value={view} onChange={setView} />
          {view === 'apps' ? (
            <AppShareList rows={appShares(summary.data.apps)} date={date} />
          ) : grouped.isPending ? (
            <p role="status">Loading groups…</p>
          ) : grouped.isError ? (
            <p role="alert">{errorMessage(grouped.error)}</p>
          ) : (
            <AppShareList
              rows={appShares(grouped.data.apps)}
              grouped
              groupHref={(name) => paths.group(date, date, name, 'day')}
            />
          )}
        </>
      )}
    </section>
  );
}
