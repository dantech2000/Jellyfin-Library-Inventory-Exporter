import { readFileSync } from 'node:fs';
import { test as base } from '@playwright/test';
import { jellyfinUrl, sessionsFile } from './env';
import { JellyfinClient, Session } from './jellyfin';
import { InventoryExporterApi } from './plugin';

export type Sessions = { admin: Session; viewer: Session };

type Fixtures = {
  sessions: Sessions;
  adminApi: JellyfinClient;
  viewerApi: JellyfinClient;
  anonymousApi: JellyfinClient;
  exporter: InventoryExporterApi;
  cleanPluginState: void;
};

export const test = base.extend<Fixtures>({
  // Written by tests/global.setup.ts.
  sessions: async ({}, use) => {
    await use(JSON.parse(readFileSync(sessionsFile, 'utf8')));
  },

  adminApi: async ({ sessions }, use) => {
    const client = await JellyfinClient.connect(jellyfinUrl, sessions.admin);
    await use(client);
    await client.dispose();
  },

  viewerApi: async ({ sessions }, use) => {
    const client = await JellyfinClient.connect(jellyfinUrl, sessions.viewer);
    await use(client);
    await client.dispose();
  },

  anonymousApi: async ({}, use) => {
    const client = await JellyfinClient.connect(jellyfinUrl);
    await use(client);
    await client.dispose();
  },

  exporter: async ({ adminApi }, use) => {
    await use(new InventoryExporterApi(adminApi));
  },

  // Every test starts with default plugin settings and no exports.
  cleanPluginState: [
    async ({ exporter }, use) => {
      await exporter.reset();
      await use();
    },
    { auto: true },
  ],
});

export { expect } from '@playwright/test';
