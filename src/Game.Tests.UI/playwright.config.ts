import { defineConfig, devices } from '@playwright/test';

const PORT = 5902;
const BASE_URL = `http://localhost:${PORT}`;

/**
 * E2E suite for the Game.Web host (static SSR + client-side PixiJS bootstrap).
 * Uses the installed Chrome (channel: 'chrome') — do NOT download bundled browsers.
 * The webServer boots the real host via `dotnet run`; set GAME_WEB_EXTERNAL_URL to
 * reuse an already-running instance instead.
 */
export default defineConfig({
  testDir: './tests',
  timeout: 30_000,
  expect: { timeout: 10_000 },
  fullyParallel: false, // the host holds singleton simulations; keep tests sequential
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.GAME_WEB_EXTERNAL_URL ?? BASE_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    ...devices['Desktop Chrome'],
    channel: 'chrome',
  },
  webServer: process.env.GAME_WEB_EXTERNAL_URL
    ? undefined
    : {
        command: `dotnet run --project ../../src/Game.Web --no-launch-profile --urls http://localhost:${PORT}`,
        url: `${BASE_URL}/`,
        reuseExistingServer: true,
        timeout: 120_000,
        env: {
          ASPNETCORE_ENVIRONMENT: 'Development',
          ASPNETCORE_SUPPRESSHTTPSREDIRECTION: 'true',
        },
      },
});
