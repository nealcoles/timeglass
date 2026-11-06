import { useQuery } from '@tanstack/react-query';
import { useApiClient } from './context.ts';
import { unwrap } from './errors.ts';

/** Query keys, one place so invalidation and tests agree on them. */
export const queryKeys = {
  status: ['status'] as const,
  day: (date: string) => ['days', date] as const,
  daySummary: (date: string) => ['days', date, 'summary'] as const,
  timeline: (date: string) => ['days', date, 'timeline'] as const,
  appDay: (date: string, appKey: string) => ['days', date, 'apps', appKey] as const,
  range: (from: string, to: string) => ['range', from, to] as const,
};

/** What the tracker is doing right now. */
export function useStatus() {
  const api = useApiClient();
  return useQuery({
    queryKey: queryKeys.status,
    queryFn: ({ signal }) => unwrap(api.GET('/api/status', { signal })),
  });
}

/** Where the time went on `date` (yyyy-MM-dd), per app. */
export function useDaySummary(date: string) {
  const api = useApiClient();
  return useQuery({
    queryKey: queryKeys.daySummary(date),
    queryFn: ({ signal }) =>
      unwrap(api.GET('/api/days/{date}/summary', { params: { path: { date } }, signal })),
  });
}

/** What happened when on `date`. */
export function useTimeline(date: string) {
  const api = useApiClient();
  return useQuery({
    queryKey: queryKeys.timeline(date),
    queryFn: ({ signal }) =>
      unwrap(api.GET('/api/days/{date}/timeline', { params: { path: { date } }, signal })),
  });
}

/** One app's time on `date`, by window title. */
export function useAppDay(date: string, appKey: string) {
  const api = useApiClient();
  return useQuery({
    queryKey: queryKeys.appDay(date, appKey),
    queryFn: ({ signal }) =>
      unwrap(
        api.GET('/api/days/{date}/apps/{appKey}', { params: { path: { date, appKey } }, signal }),
      ),
  });
}

/** Totals per day and per app from `from` to `to`, both included. */
export function useRangeSummary(from: string, to: string) {
  const api = useApiClient();
  return useQuery({
    queryKey: queryKeys.range(from, to),
    queryFn: ({ signal }) =>
      unwrap(api.GET('/api/range/summary', { params: { query: { from, to } }, signal })),
  });
}
