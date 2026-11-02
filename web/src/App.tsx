import { useEffect, useState } from 'react';

type ApiStatus = 'checking' | 'online' | 'offline';

/**
 * Placeholder shell. The real day view arrives in #32; for now the page proves the
 * build, the dev proxy and the test setup work end to end.
 */
export function App() {
  const [status, setStatus] = useState<ApiStatus>('checking');

  useEffect(() => {
    const controller = new AbortController();
    fetch('/api/status', { signal: controller.signal })
      .then((res) => setStatus(res.ok ? 'online' : 'offline'))
      .catch((err: unknown) => {
        if (!(err instanceof DOMException && err.name === 'AbortError')) {
          setStatus('offline');
        }
      });
    return () => controller.abort();
  }, []);

  return (
    <main className="app">
      <h1>Timeglass</h1>
      <p>Where did my day go?</p>
      <p role="status">Tracker: {status}</p>
    </main>
  );
}
