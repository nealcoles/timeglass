import { useStatus } from './api/queries';

type ApiStatus = 'checking' | 'online' | 'offline';

/**
 * Placeholder shell. The real day view arrives in #32; for now the page proves the
 * build, the API client and the test setup work end to end.
 */
export function App() {
  const { isPending, isError } = useStatus();
  const status: ApiStatus = isPending ? 'checking' : isError ? 'offline' : 'online';

  return (
    <main className="app">
      <h1>Timeglass</h1>
      <p>Where did my day go?</p>
      <p role="status">Tracker: {status}</p>
    </main>
  );
}
