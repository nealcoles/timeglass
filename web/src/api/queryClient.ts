import { QueryClient, type DefaultOptions } from '@tanstack/react-query';
import { ApiError } from './errors.ts';

/** How long data counts as fresh. Today's numbers grow while the dashboard is open. */
export const STALE_TIME_MS = 15_000;

const MAX_RETRIES = 2;

/** Retry network errors and server errors, but not answers that won't change, like 400 or 401. */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (error instanceof ApiError && error.status < 500) {
    return false;
  }
  return failureCount < MAX_RETRIES;
}

/** The dashboard's query client. Tests pass `{ retry: false }` to fail fast. */
export function createQueryClient(queries: DefaultOptions['queries'] = {}): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: STALE_TIME_MS,
        refetchOnWindowFocus: true,
        retry: shouldRetry,
        ...queries,
      },
    },
  });
}
