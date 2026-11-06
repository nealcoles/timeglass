import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { apps, demoDate } from '../test/fixtures.ts';
import { unauthorized } from '../test/handlers.ts';
import { renderApiHook } from '../test/render.tsx';
import { server } from '../test/server.ts';
import { ApiProvider } from './ApiProvider.tsx';
import { ApiError } from './errors.ts';
import {
  queryKeys,
  useAppDay,
  useDaySummary,
  useRangeSummary,
  useStatus,
  useTimeline,
} from './queries.ts';
import { createQueryClient } from './queryClient.ts';

function Status() {
  const { data } = useStatus();
  return <p>{data?.state}</p>;
}

describe('API hooks', () => {
  describe('success', () => {
    it('useStatus returns the tracker state', async () => {
      const { result } = renderApiHook(() => useStatus());

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data?.state).toBe('active');
      expect(result.current.data?.currentApp).toEqual(apps.code);
    });

    it('useDaySummary asks for the given date', async () => {
      const { result } = renderApiHook(() => useDaySummary('2026-11-04'));

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data?.date).toBe('2026-11-04');
      expect(result.current.data?.apps.map((a) => a.percent)).toEqual([43, 25, 18, 11, 3]);
    });

    it('useTimeline returns the blocks in order', async () => {
      const { result } = renderApiHook(() => useTimeline(demoDate));

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      const blocks = result.current.data?.blocks ?? [];
      expect(blocks.at(-1)?.isOpen).toBe(true);
      expect(blocks.some((b) => b.kind === 'away' && b.app === null)).toBe(true);
    });

    it('useAppDay puts the date and app key in the path', async () => {
      let seen: { date?: string; appKey?: string } = {};
      server.use(
        http.get('/api/days/:date/apps/:appKey', ({ params }) => {
          seen = { date: String(params.date), appKey: String(params.appKey) };
          return undefined;
        }),
      );

      const { result } = renderApiHook(() => useAppDay(demoDate, 'vscode'));

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(seen).toEqual({ date: demoDate, appKey: 'vscode' });
      expect(result.current.data?.titles).toHaveLength(2);
      expect(result.current.data?.other?.count).toBe(3);
    });

    it('useRangeSummary sends from and to as query parameters', async () => {
      const { result } = renderApiHook(() => useRangeSummary('2026-11-02', '2026-11-08'));

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data?.days).toHaveLength(7);
      expect(result.current.data?.days.filter((d) => d.activeMs > 0)).toHaveLength(5);
    });
  });

  describe('errors', () => {
    it('turns a 400 into an ApiError with the problem details', async () => {
      const { result } = renderApiHook(() => useDaySummary('yesterday'));

      await waitFor(() => expect(result.current.isError).toBe(true));
      const error = result.current.error;
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).status).toBe(400);
      expect((error as ApiError).problem?.errors).toEqual({ date: ['Use yyyy-MM-dd.'] });
    });

    it('turns a 500 without a body into an ApiError', async () => {
      server.use(
        http.get('/api/days/:date/timeline', () => new HttpResponse(null, { status: 500 })),
      );

      const { result } = renderApiHook(() => useTimeline(demoDate));

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toEqual(expect.objectContaining({ status: 500, problem: null }));
    });

    it('passes network errors through', async () => {
      server.use(http.get('/api/status', () => HttpResponse.error()));

      const { result } = renderApiHook(() => useStatus());

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toBeInstanceOf(TypeError);
    });
  });

  describe('401', () => {
    it('fails the query and signs the dashboard out', async () => {
      server.use(http.get('/api/range/summary', unauthorized));

      const { queryClient } = renderApiHook(() => useRangeSummary('2026-11-02', '2026-11-08'));

      // The provider replaces the dashboard, so the hook's own component is gone; the
      // error is still in the cache.
      expect(await screen.findByRole('alert')).toHaveTextContent('Timeglass tray icon');
      const error = queryClient.getQueryState(queryKeys.range('2026-11-02', '2026-11-08'))?.error;
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).isUnauthorized).toBe(true);
    });

    it('does not retry, even with retries on', async () => {
      let calls = 0;
      server.use(
        http.get('/api/status', () => {
          calls += 1;
          return unauthorized();
        }),
      );
      const queryClient = createQueryClient();
      render(
        <ApiProvider queryClient={queryClient}>
          <Status />
        </ApiProvider>,
      );

      expect(await screen.findByRole('alert')).toBeInTheDocument();
      expect(calls).toBe(1);
    });
  });
});
