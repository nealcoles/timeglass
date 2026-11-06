import { useStatus } from './api/queries';
import { DayView } from './day/DayView.tsx';
import { localToday } from './format.ts';

type ApiStatus = 'checking' | 'online' | 'offline';

/**
 * The dashboard shell: the tracker's state and today's day view. Today is the tracker's own date
 * (from `/api/status`), falling back to the browser's date when the tracker can't be reached.
 */
export function App() {
  const { data, isPending, isError } = useStatus();
  const status: ApiStatus = isPending ? 'checking' : isError ? 'offline' : 'online';
  const today = data?.today ?? localToday();

  return (
    <main className="app">
      <header className="app__header">
        <h1>Timeglass</h1>
        <p role="status" className="app__tracker">
          Tracker: {status}
        </p>
      </header>
      {!isPending && <DayView date={today} isToday />}
    </main>
  );
}
