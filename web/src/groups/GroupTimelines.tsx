import type { RangeSummaryDto } from '../api/client.ts';
import { DayTimeline } from '../day/DayTimeline.tsx';
import { formatLongDate } from '../format.ts';

export function GroupTimelines({
  summary,
  groupName,
  colorFor,
  appKey,
}: {
  summary: RangeSummaryDto;
  groupName: string;
  colorFor: (key: string) => string;
  appKey?: string;
}) {
  const days = summary.days.filter((day) =>
    appKey ? day.apps.some((app) => app.app.key === appKey) : day.activeMs > 0,
  );
  return (
    <>
      <h3>When</h3>
      {days.map((day) => (
        <div key={day.date}>
          {summary.from !== summary.to && <h4>{formatLongDate(day.date)}</h4>}
          <DayTimeline
            date={day.date}
            grouped
            colorFor={colorFor}
            includeBlock={(block) =>
              block.kind === 'active' &&
              block.group?.name.toLowerCase() === groupName.toLowerCase() &&
              (appKey === undefined || block.app?.key === appKey)
            }
          />
        </div>
      ))}
    </>
  );
}
