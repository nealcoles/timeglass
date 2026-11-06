import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { server } from './server.ts';

// Relative URLs like '/api/status' need a base URL in Node.
const baseUrl = 'http://localhost';
const originalFetch = globalThis.fetch;
globalThis.fetch = (input: RequestInfo | URL, init?: RequestInit) =>
  originalFetch(typeof input === 'string' && input.startsWith('/') ? baseUrl + input : input, init);

// jsdom doesn't scroll; navigating scrolls to the top.
globalThis.scrollTo = () => {};

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }));
afterEach(() => {
  cleanup();
  server.resetHandlers();
  // Each test starts on the dashboard's home page.
  globalThis.history.replaceState(null, '', '/');
});
afterAll(() => server.close());
