import { defineConfig, devices } from '@playwright/test';

const port = Number(process.env.E2E_PORT || 5051);
const baseURL = `http://127.0.0.1:${port}`;

export default defineConfig({
  testDir: './tests',
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL,
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: `dotnet run --no-launch-profile --project ../backend/src/InvoiceApp.Api --urls ${baseURL}`,
    url: baseURL,
    reuseExistingServer: false,
    timeout: 120_000,
  },
});
