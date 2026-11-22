import { useStatus } from './api/queries';
import { AppView } from './app/AppView.tsx';
import { DayView } from './day/DayView.tsx';
import { localToday } from './format.ts';
import { Link } from './Link.tsx';
import { paths, useRoute, type Route } from './router.ts';
import { SettingsView } from './settings/SettingsView.tsx';
import { TrackerControl } from './status/TrackerControl.tsx';
import { TrendsView } from './trends/TrendsView.tsx';
import { WeekView } from './week/WeekView.tsx';

type ApiStatus = 'checking' | 'online' | 'offline';

/**
 * The dashboard shell: the tracker's state and the screen the URL names. Today is the tracker's
 * own date (from `/api/status`), falling back to the browser's date when the tracker can't be
 * reached.
 */
export function App() {
  const route = useRoute();
  const { data, isPending, isError } = useStatus();
  const status: ApiStatus = isPending ? 'checking' : isError ? 'offline' : 'online';
  const today = data?.today ?? localToday();

  return (
    <main className="app">
      <header className="app__header">
        <h1>
          <Link href={paths.today()} className="app__home">
            Timeglass
          </Link>
        </h1>
        <div className="app__status">
          <p role="status" className="app__tracker">
            Tracker: {status}
          </p>
          {data && <TrackerControl status={data} />}
          <Link
            href={paths.trends()}
            className="app__settings"
            aria-current={route.name === 'trends' ? 'page' : undefined}
          >
            Trends
          </Link>
          <Link
            href={paths.settings()}
            className="app__settings"
            aria-current={route.name === 'settings' ? 'page' : undefined}
          >
            Settings
          </Link>
        </div>
      </header>
      {!isPending && <Screen route={route} today={today} />}
    </main>
  );
}

function Screen({ route, today }: { route: Route; today: string }) {
  switch (route.name) {
    case 'today':
      return <DayView date={today} today={today} />;
    case 'day':
      return <DayView date={route.date} today={today} />;
    case 'app':
      return <AppView date={route.date} appKey={route.appKey} isToday={route.date === today} />;
    case 'thisWeek':
      return <WeekView date={today} today={today} />;
    case 'week':
      return <WeekView date={route.date} today={today} />;
    case 'trends':
      return <TrendsView today={today} />;
    case 'settings':
      return <SettingsView today={today} />;
    case 'notFound':
      return (
        <section className="day">
          <h2>Page not found</h2>
          <p>
            <Link href={paths.today()}>Go to today</Link>
          </p>
        </section>
      );
  }
}
