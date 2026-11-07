import { fileURLToPath } from 'node:url';
import { defineConfig, devices } from '@playwright/test';
import { NOW, TIME_ZONE } from './e2e/demo.ts';

/**
 * End-to-end tests (#37): the built dashboard and the real API together, served by the dev host
 * over its demo data with the clock stopped, so every run sees the same day.
 *
 * Build the dashboard first (`npm run build`), then `npm run e2e`. Playwright starts the dev host
 * itself, or reuses one already listening on the port outside CI.
 */

const port = Number(process.env.TIMEGLASS_E2E_PORT ?? 5410);
const baseURL = `http://127.0.0.1:${port}`;
// Absolute: `dotnet run` starts the dev host in its own project folder.
const dist = fileURLToPath(new URL('./dist', import.meta.url));

export default defineConfig({
  testDir: './e2e',
  // One dev host behind all tests, and some of them change its state (pause, settings).
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL,
    locale: 'en-GB',
    timezoneId: TIME_ZONE,
    // Kept on failure, uploaded by CI, opened with `npx playwright show-trace`.
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        // For machines with Chromium already installed somewhere else.
        launchOptions: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE
          ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE }
          : {},
      },
    },
  ],
  webServer: {
    command: [
      'dotnet run --project ../tools/Timeglass.DevHost -c Release --',
      `--port ${port} --now ${NOW} --timeZone ${TIME_ZONE} --web "${dist}"`,
    ].join(' '),
    url: `${baseURL}/api/status`,
    reuseExistingServer: !process.env.CI,
    // The first run builds the dev host.
    timeout: 180_000,
    stdout: 'pipe',
    stderr: 'pipe',
  },
});
