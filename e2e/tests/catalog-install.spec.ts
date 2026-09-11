import { expect, test } from '@playwright/test';
import { admin, catalogUrl, expectedPluginVersion, jellyfinCleanUrl, line } from '../lib/env';
import { completeStartupWizard, JellyfinClient, normalizeId, signIn, waitForHealthy } from '../lib/jellyfin';
import { PLUGIN_ID, PLUGIN_NAME } from '../lib/plugin';
import { openConfigPage, signInThroughWebUi } from '../lib/ui';

// Runs against `jellyfin-clean`, a stock server, so it does not use the shared plugin fixtures.
test.describe('plugin catalog', () => {
  test('installs the build for this Jellyfin line from a plugin repository', async ({ browser }) => {
    test.setTimeout(10 * 60_000);

    await waitForHealthy(jellyfinCleanUrl);
    await completeStartupWizard(jellyfinCleanUrl, admin);
    const client = await JellyfinClient.connect(jellyfinCleanUrl, await signIn(jellyfinCleanUrl, admin));
    const installedPlugin = async () =>
      (await client.get<any[]>('/Plugins')).find(plugin => normalizeId(plugin.Id) === normalizeId(PLUGIN_ID));

    try {
      const repositoryUrl = `${catalogUrl}/manifest.json`;
      await client.post('/Repositories', [{ Name: 'E2E catalog', Url: repositoryUrl, Enabled: true }]);

      // Jellyfin lists only the versions whose targetAbi the server supports.
      const catalogPackage = (await client.get<any[]>('/Packages')).find(candidate => normalizeId(candidate.guid) === normalizeId(PLUGIN_ID));
      expect(catalogPackage, `${PLUGIN_NAME} in the catalog`).toMatchObject({ name: PLUGIN_NAME });
      const offered = catalogPackage.versions.map((version: any) => `${version.version}@${version.targetAbi}`).sort();
      const build1011 = `${expectedPluginVersion.replace(/\.\d+$/, '.10')}@10.11.0.0`;
      const build12 = `${expectedPluginVersion.replace(/\.\d+$/, '.12')}@12.0.0.0`;
      expect(offered).toEqual(line.name === '12' ? [build1011, build12].sort() : [build1011]);

      if ((await installedPlugin())?.Version !== expectedPluginVersion) {
        // No version: Jellyfin picks the newest build whose targetAbi the server supports.
        await client.post(`/Packages/Installed/${encodeURIComponent(PLUGIN_NAME)}`, undefined, { assemblyGuid: PLUGIN_ID, repositoryUrl });
        await expect.poll(async () => (await installedPlugin())?.Version, { message: 'package installs', timeout: 120_000 }).toBe(expectedPluginVersion);
        await client.post('/System/Restart');
      }

      await expect
        .poll(
          async () => {
            try {
              const plugin = await installedPlugin();
              return `${plugin?.Version} ${plugin?.Status}`;
            } catch (error) {
              return (error as Error).message.split('\n')[0];
            }
          },
          { message: 'plugin is active after the restart', timeout: 300_000, intervals: [2_000] },
        )
        .toBe(`${expectedPluginVersion} Active`);
      await waitForHealthy(jellyfinCleanUrl);
    } finally {
      await client.dispose();
    }

    const context = await browser.newContext({ baseURL: jellyfinCleanUrl });
    try {
      const page = await context.newPage();
      await signInThroughWebUi(page, admin);
      const root = await openConfigPage(page);
      await expect(root.locator('#libraryList')).toContainText('No libraries found');
    } finally {
      await context.close();
    }
  });
});
