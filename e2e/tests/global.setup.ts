import { mkdirSync, writeFileSync } from 'node:fs';
import { expect, test as setup } from '@playwright/test';
import { admin, adminStorageState, authDir, expectedPluginVersion, jellyfinUrl, line, sessionsFile, viewer } from '../lib/env';
import {
  completeStartupWizard,
  ensureLibraries,
  ensureUser,
  findItem,
  JellyfinClient,
  normalizeId,
  scanLibraries,
  signIn,
  waitForHealthy,
} from '../lib/jellyfin';
import { PLUGIN_ID } from '../lib/plugin';
import { signInThroughWebUi } from '../lib/ui';

// Idempotent: safe to rerun against a server that is already provisioned (KEEP=1).
setup('provision Jellyfin with the fixture libraries and users', async ({ page }) => {
  setup.setTimeout(10 * 60_000);

  await waitForHealthy(jellyfinUrl);
  await completeStartupWizard(jellyfinUrl, admin);
  const adminSession = await signIn(jellyfinUrl, admin);
  const client = await JellyfinClient.connect(jellyfinUrl, adminSession);
  try {
    const plugins = await client.get<any[]>('/Plugins');
    const plugin = plugins.find(candidate => normalizeId(candidate.Id) === normalizeId(PLUGIN_ID));
    expect(plugin, `side-loaded plugin is registered. Plugins: ${JSON.stringify(plugins)}`).toBeTruthy();
    expect(plugin.Status, 'plugin status').toBe('Active');
    expect(plugin.Version, `plugin build for Jellyfin ${line.name}`).toBe(expectedPluginVersion);

    await ensureLibraries(client);
    await scanLibraries(client, adminSession.userId);
    await ensureUser(client, viewer);

    const movie = await findItem(client, adminSession.userId, 'Movie', 'Fixture Movie');
    await client.post(`/UserPlayedItems/${movie.Id}`, undefined, { userId: adminSession.userId });
    await client.post(`/UserFavoriteItems/${movie.Id}`, undefined, { userId: adminSession.userId });
  } finally {
    await client.dispose();
  }

  const viewerSession = await signIn(jellyfinUrl, viewer);
  mkdirSync(authDir, { recursive: true });
  writeFileSync(sessionsFile, JSON.stringify({ admin: adminSession, viewer: viewerSession }, null, 2));

  await signInThroughWebUi(page, admin);
  await page.context().storageState({ path: adminStorageState });
});
