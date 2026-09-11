import { expect, test } from '../lib/fixtures';
import { expectedPluginVersion, line } from '../lib/env';
import { normalizeId } from '../lib/jellyfin';
import { PLUGIN_ID, PLUGIN_NAME } from '../lib/plugin';
import { waitForConfigPage } from '../lib/ui';

test.describe('plugin install', () => {
  test('loads the build for this Jellyfin line', async ({ adminApi }) => {
    const server = await adminApi.get('/System/Info');
    expect(server.Version).toMatch(new RegExp(`^${line.serverVersionPrefix.replaceAll('.', '\\.')}`));

    const plugin = (await adminApi.get<any[]>('/Plugins')).find(candidate => normalizeId(candidate.Id) === normalizeId(PLUGIN_ID));
    expect(plugin).toMatchObject({ Name: PLUGIN_NAME, Version: expectedPluginVersion, Status: 'Active' });

    const pages = (await adminApi.get<any[]>('/web/ConfigurationPages')).filter(page => normalizeId(page.PluginId) === normalizeId(PLUGIN_ID));
    expect(pages.map(page => page.Name).sort()).toEqual(['LibraryInventoryExporter', 'LibraryInventoryExporterJs']);
  });

  test('opens its settings from Dashboard > Plugins', async ({ page }) => {
    await page.goto('/web/#/dashboard/plugins');
    // The card links from its image area, not from the title below it.
    await page.locator(`a[href*="dashboard/plugins/"][href*="name=${encodeURIComponent(PLUGIN_NAME)}"]`).first().click();
    await expect(page).toHaveURL(/dashboard\/plugins\/[0-9a-f-]{32,36}/i);

    const settings = page.getByRole('link', { name: 'Settings' }).or(page.getByRole('button', { name: 'Settings' }));
    await settings.first().click();
    await expect(page).toHaveURL(/configurationpage\?name=LibraryInventoryExporter/);
    await waitForConfigPage(page);
  });

  test('lists its scheduled task on the dashboard', async ({ page }) => {
    await page.goto('/web/#/dashboard/tasks');
    await expect(page.getByText('Library Inventory Exporter', { exact: true })).toBeVisible();
    await expect(page.getByText('Export library inventory', { exact: true })).toBeVisible();
  });
});
