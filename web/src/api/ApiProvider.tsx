import { QueryClientProvider, type QueryClient } from '@tanstack/react-query';
import { useEffect, useState, type ReactNode } from 'react';
import { createApiClient, type ApiClient } from './client.ts';
import { ApiClientContext } from './context.ts';
import { ApiError, errorMessage, isUnauthorized } from './errors.ts';
import { createQueryClient } from './queryClient.ts';

interface ApiProviderProps {
  children: ReactNode;
  client?: ApiClient;
  queryClient?: QueryClient;
}

/**
 * Gives its children the API client and the query cache, and handles 401 in one place: when any
 * query is turned away, the dashboard is replaced by a note to open it from the tray icon, which
 * signs the browser in again. Other errors are left to the component that made the query.
 */
export function ApiProvider({ children, client, queryClient }: ApiProviderProps) {
  const [api] = useState(() => client ?? createApiClient());
  const [queries] = useState(() => queryClient ?? createQueryClient());
  const [signedOut, setSignedOut] = useState(false);

  useEffect(
    () =>
      queries.getQueryCache().subscribe((event) => {
        if (
          event.type === 'updated' &&
          event.action.type === 'error' &&
          isUnauthorized(event.action.error)
        ) {
          setSignedOut(true);
        }
      }),
    [queries],
  );

  if (signedOut) {
    return (
      <main className="app">
        <h1>Timeglass</h1>
        <p role="alert">{errorMessage(new ApiError(401, null))}</p>
      </main>
    );
  }

  return (
    <ApiClientContext.Provider value={api}>
      <QueryClientProvider client={queries}>{children}</QueryClientProvider>
    </ApiClientContext.Provider>
  );
}
