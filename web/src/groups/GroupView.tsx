import { ApiError, errorMessage } from '../api/errors.ts';
import { useGroupApplications } from '../api/queries.ts';
import { formatDateRange } from '../dates.ts';
import { AppShareList } from '../day/AppShareList.tsx';
import { appShares } from '../day/appShares.ts';
import { formatDuration, formatLongDate } from '../format.ts';
import { Link } from '../Link.tsx';
import { paths, type GroupSource } from '../router.ts';

export function GroupView({
  from,
  to,
  groupName,
  source,
}: {
  from: string;
  to: string;
  groupName: string;
  source: GroupSource;
}) {
  const applications = useGroupApplications(from, to, groupName);
  const date = from === to ? formatLongDate(from) : formatDateRange(from, to);
  return (
    <section className="app-day" aria-labelledby="group-heading" aria-busy={applications.isPending}>
      <p className="app-day__back">
        <Link href={paths.groupBack(from, to, source)}>
          ← Back to {source === 'day' ? 'the day' : source === 'week' ? 'the week' : 'trends'}
        </Link>
      </p>
      <h2 id="group-heading">{groupName}</h2>
      <p className="app-day__date">{date}</p>
      {applications.isPending ? (
        <p role="status" className="app-day__loading">
          Loading applications…
        </p>
      ) : applications.isError ? (
        <p role="alert" className="app-day__error">
          {applications.error instanceof ApiError
            ? (applications.error.problem?.errors?.name?.[0] ?? errorMessage(applications.error))
            : errorMessage(applications.error)}
        </p>
      ) : applications.data.apps.length === 0 ? (
        <p className="app-day__empty">No time matched this group in the selected period.</p>
      ) : (
        <>
          <dl className="day__totals">
            <div>
              <dt>Active in group</dt>
              <dd>{formatDuration(applications.data.activeMs)}</dd>
            </div>
          </dl>
          <h3>Applications</h3>
          <AppShareList
            rows={appShares(applications.data.apps)}
            appHref={(appKey) => paths.groupApp(from, to, groupName, source, appKey)}
          />
        </>
      )}
    </section>
  );
}
