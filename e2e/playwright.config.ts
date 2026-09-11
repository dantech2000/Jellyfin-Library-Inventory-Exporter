import { defineConfig } from '@playwright/test';
import { adminStorageState, jellyfinUrl, line } from './lib/env';

// The plugin has one export gate and one global configuration, so tests run one at a time.
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: `test-results/${line.name}`,
  reporter: [['list'], ['html', { outputFolder: `playwright-report/${line.name}`, open: 'never' }]],
  use: {
    baseURL: jellyfinUrl,
    acceptDownloads: true,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    { name: 'setup', testMatch: /.*\.setup\.ts/ },
    {
      name: `jellyfin-${line.name}`,
      testMatch: /.*\.spec\.ts/,
      dependencies: ['setup'],
      use: { storageState: adminStorageState },
    },
  ],
});
