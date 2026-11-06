import { createContext, useContext } from 'react';
import type { ApiClient } from './client.ts';

export const ApiClientContext = createContext<ApiClient | null>(null);

/** The API client of the nearest {@link ApiProvider}. */
export function useApiClient(): ApiClient {
  const client = useContext(ApiClientContext);
  if (!client) {
    throw new Error('useApiClient must be used inside <ApiProvider>.');
  }
  return client;
}
