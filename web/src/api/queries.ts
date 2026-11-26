import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { PauseDuration, SettingsDto } from './client.ts';
import { useApiClient } from './context.ts';
import { unwrap } from './errors.ts';

/** Query keys, one place so invalidation and tests agree on them. */
export const queryKeys = {
  status: ['status'] as const,
  day: (date: string) => ['days', date] as const,
  daySummary: (date: string) => ['days', date, 'summary'] as const,
  timeline: (date: string, grouped = false) => ['days', date, 'timeline', { grouped }] as const,
  appDay: (date: string, appKey: string) => ['days', date, 'apps', appKey] as const,
  range: (from: string, to: string) => ['range', from, to] as const,
  groupedRange: (from: string, to: string) => ['groups', from, to] as const,
  groupApplications: (from: string, to: string, name: string) =>
    ['groups', 'applications', from, to, name] as const,
  groupAppTitles: (from: string, to: string, name: string, appKey: string) =>
    ['groups', 'titles', from, to, name, appKey] as const,
  settings: ['settings'] as const,
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
export function useTimeline(date: string, grouped = false) {
  const api = useApiClient();
  return useQuery({
    queryKey: queryKeys.timeline(date, grouped),
    queryFn: ({ signal }) =>
      unwrap(
        api.GET('/api/days/{date}/timeline', {
          params: { path: { date }, query: grouped ? { grouped: true } : {} },
          signal,
        }),
      ),
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
export function useRangeSummary(from: string, to: string, { enabled = true } = {}) {
  const api = useApiClient();
  return useQuery({
    queryKey: queryKeys.range(from, to),
    enabled,
    queryFn: ({ signal }) =>
      unwrap(api.GET('/api/range/summary', { params: { query: { from, to } }, signal })),
  });
}

export function useGroupedRangeSummary(from: string, to: string, { enabled = true } = {}) {
  const api = useApiClient();
  return useQuery({
    queryKey: queryKeys.groupedRange(from, to),
    enabled,
    queryFn: ({ signal }) =>
      unwrap(api.GET('/api/range/groups', { params: { query: { from, to } }, signal })),
  });
}

export function useGroupApplications(from: string, to: string, name: string) {
  const api = useApiClient();
  return useQuery({
    queryKey: queryKeys.groupApplications(from, to, name),
    queryFn: ({ signal }) =>
      unwrap(
        api.GET('/api/range/group-applications', { params: { query: { from, to, name } }, signal }),
      ),
  });
}

export function useGroupAppTitles(from: string, to: string, name: string, appKey: string) {
  const api = useApiClient();
  return useQuery({
    queryKey: queryKeys.groupAppTitles(from, to, name, appKey),
    queryFn: ({ signal }) =>
      unwrap(
        api.GET('/api/range/group-application-titles', {
          params: { query: { from, to, name, appKey } },
          signal,
        }),
      ),
  });
}

/** The settings the dashboard can change (#36). */
export function useSettings() {
  const api = useApiClient();
  return useQuery({
    queryKey: queryKeys.settings,
    queryFn: ({ signal }) => unwrap(api.GET('/api/settings', { signal })),
  });
}

/**
 * Saves the settings. The API answers with the settings now in effect, which replace the cached
 * ones; a 400 carries the errors per field (see `ApiError.problem.errors`).
 */
export function useSaveSettings() {
  const api = useApiClient();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (settings: SettingsDto) => unwrap(api.PUT('/api/settings', { body: settings })),
    onSuccess: async (saved) => {
      queryClient.setQueryData(queryKeys.settings, saved);
      await queryClient.invalidateQueries({ queryKey: ['groups'] });
      await queryClient.invalidateQueries({ queryKey: ['days'] });
    },
  });
}

/** Pauses or resumes tracking; the answer is the new status. */
export function usePauseTracking() {
  const api = useApiClient();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (duration: PauseDuration | null) =>
      unwrap(
        duration === null
          ? api.POST('/api/resume')
          : api.POST('/api/pause', { body: { duration } }),
      ),
    onSuccess: async (status) => {
      queryClient.setQueryData(queryKeys.status, status);
      // Pausing closes the open interval, so today's numbers stop growing.
      await queryClient.invalidateQueries({ queryKey: ['days', status.today] });
    },
  });
}

/** Deletes everything recorded from `from` to `to` (both included), then refetches every report. */
export function useDeleteRange() {
  const api = useApiClient();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ from, to }: { from: string; to: string }) =>
      unwrap(
        api.DELETE('/api/intervals', {
          params: { query: { from, to } },
          body: { confirm: `${from}..${to}` },
        }),
      ),
    onSuccess: () =>
      queryClient.invalidateQueries({ predicate: (query) => query.queryKey[0] !== 'settings' }),
  });
}
