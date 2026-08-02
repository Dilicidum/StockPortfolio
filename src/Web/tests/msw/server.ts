import { setupServer } from 'msw/node'

/**
 * One server for the whole run, started in tests/setup.ts. Individual tests add
 * their own handlers with `server.use(...)`; `resetHandlers()` between tests
 * removes them, so no test can leak a stub into the next one.
 */
export const server = setupServer()
