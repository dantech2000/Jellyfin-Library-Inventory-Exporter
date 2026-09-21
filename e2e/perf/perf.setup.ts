import { mkdirSync, writeFileSync } from 'node:fs';
import { expect, test as setup } from '@playwright/test';
import { admin, adminStorageState, authDir, expectedPluginVersion, jellyfinUrl, line, sessionsFile, viewer } from '../lib/env';
import {
  completeStartupWizard,
  ensureLibraries,
  ensureUser,
  JellyfinClient,
  normalizeId,
  signIn,
  SUPPORTED_ITEM_TYPES,
  waitForHealthy,
} from '../lib/jellyfin';
import { perfProfile, perfUsers, readSeed, scanAndWait, seededItems, updateResults } from '../lib/perf';
import { PLUGIN_ID } from '../lib/plugin';
import { signInThroughWebUi } from '../lib/ui';

// Watch state for this many items per user, so the user data export is not only default values.
const WATCHED_ITEMS_PER_USER = 20;

setup('provision Jellyfin with the seeded library', async ({ page }) => {
  setup.setTimeout(120 * 60_000);
  const seed = readSeed();

  await waitForHealthy(jellyfinUrl);
  await completeStartupWizard(jellyfinUrl, admin);
  const adminSession = await signIn(jellyfinUrl, admin);
  const client = await JellyfinClient.connect(jellyfinUrl, adminSession);
  try {
    const plugin = (await client.get<any[]>('/Plugins')).find(candidate => normalizeId(candidate.Id) === normalizeId(PLUGIN_ID));
    expect(plugin, 'the side-loaded plugin is registered').toMatchObject({ Status: 'Active', Version: expectedPluginVersion });
    const server = await client.get('/System/Info');

    await ensureLibraries(client);
    const countIndexed = async () =>
      (
        await client.get('/Items', {
          userId: adminSession.userId,
          recursive: true,
          includeItemTypes: SUPPORTED_ITEM_TYPES.join(','),
          limit: 0,
        })
      ).TotalRecordCount as number;

    // With REUSE=1 the database of an earlier run already holds the seeded library, so there is nothing to scan.
    const scanReused = (await countIndexed()) >= seededItems(seed);
    const scanSeconds = scanReused ? 0 : await scanAndWait(client, adminSession.userId);
    const indexed = { TotalRecordCount: await countIndexed() };

    await ensureUser(client, viewer);
    for (let index = 1; index <= perfUsers; index++) {
      await ensureUser(client, { name: `perf-user-${String(index).padStart(2, '0')}`, password: 'perf-password' });
    }

    const users = await client.get<any[]>('/Users');
    const watched = await client.get('/Items', {
      userId: adminSession.userId,
      recursive: true,
      includeItemTypes: 'Movie,Episode',
      limit: WATCHED_ITEMS_PER_USER,
    });
    for (const user of users) {
      for (const item of watched.Items) {
        await client.post(`/UserPlayedItems/${item.Id}`, undefined, { userId: user.Id });
      }
    }

    updateResults(results => {
      Object.assign(results, {
        line: line.name,
        profile: perfProfile,
        jellyfinVersion: server.Version,
        pluginVersion: expectedPluginVersion,
        platform: process.env.JELLYFIN_PLATFORM ?? 'native',
        seed,
        seededItems: seededItems(seed),
        indexedItems: indexed.TotalRecordCount,
        scanSeconds,
        scanReused,
        users: users.length,
        exports: [],
      });
    });
  } finally {
    await client.dispose();
  }

  const viewerSession = await signIn(jellyfinUrl, viewer);
  mkdirSync(authDir, { recursive: true });
  writeFileSync(sessionsFile, JSON.stringify({ admin: adminSession, viewer: viewerSession }, null, 2));

  await signInThroughWebUi(page, admin);
  await page.context().storageState({ path: adminStorageState });
});
