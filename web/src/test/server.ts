import { setupServer } from 'msw/node';
import { handlers } from './handlers.ts';

/** Shared MSW server on demo data. Tests override handlers with `server.use(...)`. */
export const server = setupServer(...handlers);
