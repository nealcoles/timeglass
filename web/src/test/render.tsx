import { render, renderHook, type RenderOptions } from '@testing-library/react';
import type { ReactElement, ReactNode } from 'react';
import { ApiProvider } from '../api/ApiProvider.tsx';
import { createQueryClient } from '../api/queryClient.ts';

/** A fresh cache per test, without retries so failures show at once. */
export function createTestQueryClient() {
  return createQueryClient({ retry: false, gcTime: Infinity });
}

function createWrapper(queryClient = createTestQueryClient()) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return <ApiProvider queryClient={queryClient}>{children}</ApiProvider>;
  };
}

/** Renders `ui` inside an {@link ApiProvider} talking to the MSW server. */
export function renderWithApi(ui: ReactElement, options: Omit<RenderOptions, 'wrapper'> = {}) {
  return render(ui, { ...options, wrapper: createWrapper() });
}

/** Renders a hook inside an {@link ApiProvider} talking to the MSW server. */
export function renderApiHook<T>(hook: () => T) {
  const queryClient = createTestQueryClient();
  return { ...renderHook(hook, { wrapper: createWrapper(queryClient) }), queryClient };
}
