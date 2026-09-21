import { defineConfig } from '@playwright/test';
import { adminStorageState, jellyfinUrl, line } from './lib/env';

// Performance suite for a seeded library of thousands of items. Run it with e2e/perf.sh.
export default defineConfig({
  testDir: './perf',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 60 * 60_000,
  expect: { timeout: 30_000 },
  outputDir: `test-results/perf-${line.name}`,
  reporter: [['list'], ['html', { outputFolder: `playwright-report/perf-${line.name}`, open: 'never' }]],
  use: {
    baseURL: jellyfinUrl,
    acceptDownloads: true,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'perf-setup', testMatch: /.*\.setup\.ts/ },
    {
      name: `perf-${line.name}`,
      testMatch: /.*\.perf\.ts/,
      dependencies: ['perf-setup'],
      use: { storageState: adminStorageState },
    },
  ],
});
