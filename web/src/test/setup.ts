import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { server } from './server.ts';

// Relative URLs like '/api/status' need a base URL in Node.
const baseUrl = 'http://localhost';
const originalFetch = globalThis.fetch;
globalThis.fetch = (input: RequestInfo | URL, init?: RequestInit) =>
  originalFetch(typeof input === 'string' && input.startsWith('/') ? baseUrl + input : input, init);

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }));
afterEach(() => {
  cleanup();
  server.resetHandlers();
});
afterAll(() => server.close());
