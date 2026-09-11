import { expect, test } from '../lib/fixtures';
import { EXPORTS_VOLUME_DIRECTORY } from '../lib/plugin';
import { CONFIG_PAGE_URL, openConfigPage, runExportFromPage, toast, waitForConfigPage } from '../lib/ui';

// The media volume is mounted read-only, so Jellyfin cannot write here.
const READ_ONLY_DIRECTORY = '/media/inventory-exports';

test.describe('error handling', () => {
  test('explains an unwritable output directory and recovers after the user picks another', async ({ page, exporter }) => {
    await exporter.saveConfig({ OutputDirectory: READ_ONLY_DIRECTORY });
    const root = await openConfigPage(page);
    const error = root.locator('#inventoryExporterError');
    await expect(root.locator('#outputDirectoryWarning')).toHaveText(
      `Jellyfin cannot write to ${READ_ONLY_DIRECTORY}. Exports will fail until you choose a directory that the server can write to and save the settings.`,
    );

    await root.locator('#btnRunExport').click();
    await expect(error).toHaveText(
      `Could not start the export: Jellyfin cannot write to ${READ_ONLY_DIRECTORY}. Choose an output directory that the Jellyfin server can write to, then save the settings.`,
    );
    await expect(root.locator('#btnRunExport')).toBeEnabled();
    expect(await exporter.exports()).toEqual([]);

    await root.locator(`[data-output-directory="${EXPORTS_VOLUME_DIRECTORY}"]`).click();
    await root.getByRole('button', { name: 'Save' }).click();
    await expect(toast(page, /Settings saved/i)).toBeVisible();
    await expect(root.locator('#outputDirectoryWarning')).toBeHidden();
    await expect(error).toBeHidden();

    const exportId = await runExportFromPage(page, root);
    await expect(error).toBeHidden();
    expect((await exporter.exports()).map(entry => entry.id)).toEqual([exportId]);
  });

  test('rejects an export it cannot write, then keeps working', async ({ exporter }) => {
    await exporter.saveConfig({ OutputDirectory: READ_ONLY_DIRECTORY });
    const rejected = await exporter.client.fetch('POST', '/InventoryExporter/Export', { data: {} });
    expect(rejected.status()).toBe(400);
    expect(await rejected.json()).toMatchObject({
      title: 'Output directory is not writable',
      detail: `Jellyfin cannot write to ${READ_ONLY_DIRECTORY}. Choose an output directory that the Jellyfin server can write to, then save the settings.`,
    });

    // A rejected export must not leave the plugin stuck in "an export is already running".
    await exporter.saveConfig({ OutputDirectory: '' });
    await exporter.runExport();
  });

  test('explains unknown formats and export ids in API responses', async ({ adminApi }) => {
    const format = await adminApi.fetch('POST', '/InventoryExporter/Export', { data: { formats: ['xml'] } });
    expect(format.status()).toBe(400);
    expect((await format.json()).detail).toBe('"xml" is not an export format. Use "csv" or "json".');

    const invalid = await adminApi.fetch('GET', '/InventoryExporter/Exports/not-an-export/Download');
    expect(invalid.status()).toBe(400);
    expect((await invalid.json()).detail).toBe('"not-an-export" is not an export id. Export ids look like 2026-05-16T123456Z.');

    const missing = await adminApi.fetch('GET', '/InventoryExporter/Exports/2000-01-01T000000Z/Download');
    expect(missing.status()).toBe(404);
    expect((await missing.json()).detail).toBe(
      'Export 2000-01-01T000000Z does not exist. Retention may have deleted it. Refresh the page to see the current exports.',
    );

    const latest = await adminApi.fetch('GET', '/InventoryExporter/Exports/Latest');
    expect(latest.status()).toBe(404);
    expect((await latest.json()).detail).toBe('There is no export to download yet. Run an export first.');
  });

  test('shows a failed export in the panel, also after a reload', async ({ page }) => {
    const failed = { IsRunning: false, ExportId: '2026-01-01T000000Z', Stage: 'Failed', ProgressPercent: 12, ProcessedItems: 1, TotalItems: 8, ErrorMessage: 'Disk full' };
    const root = await openConfigPage(page);
    await page.route('**/InventoryExporter/Export', route => route.fulfill({ json: { ExportId: failed.ExportId, Status: 'running' } }));
    await page.route('**/InventoryExporter/Status', route => route.fulfill({ json: failed }));

    await root.locator('#btnRunExport').click();
    await expect(toast(page, 'Library inventory export failed: Disk full')).toBeVisible();
    await expect(root.locator('#inventoryExporterError')).toHaveText('Library inventory export failed: Disk full');
    await expect(root.locator('#exportProgressDetails')).toHaveText('Disk full');
    await expect(root.locator('#exportStatus')).toHaveText('Failed 12%: Disk full');
    await expect(root.locator('#btnRunExport')).toBeEnabled();

    // An administrator who opens the page later still sees why the last export failed.
    await page.reload();
    const reloaded = await waitForConfigPage(page);
    await expect(reloaded.locator('#inventoryExporterError')).toHaveText('Library inventory export failed: Disk full');
  });

  test('explains why the page could not load its data', async ({ page }) => {
    await page.route('**/InventoryExporter/Libraries', route =>
      route.fulfill({ status: 500, json: { title: 'Internal Server Error', detail: 'The library database is locked.' } }),
    );
    await page.goto(CONFIG_PAGE_URL);
    const root = page.locator('#LibraryInventoryExporterConfigPage');
    await expect(root.locator('#inventoryExporterError')).toHaveText('Could not load the libraries: The library database is locked.');
  });

  test('explains a failed save and clears the loading indicator', async ({ page }) => {
    const root = await openConfigPage(page);
    await page.route(/\/Plugins\/[0-9a-f-]+\/Configuration$/i, route =>
      route.request().method() === 'POST'
        ? route.fulfill({ status: 500, json: { title: 'Internal Server Error', detail: 'The configuration file is read-only.' } })
        : route.continue(),
    );

    await root.getByRole('button', { name: 'Save' }).click();
    await expect(root.locator('#inventoryExporterError')).toHaveText('Could not save the settings: The configuration file is read-only.');
    await expect(page.locator('.docspinner')).toBeHidden();
  });

  test('says there is nothing to download before the first export', async ({ page }) => {
    const root = await openConfigPage(page);
    const url = page.url();

    await root.locator('#btnDownloadLatest').click();
    await expect(root.locator('#inventoryExporterError')).toHaveText('There is no export to download yet. Run an export first.');
    expect(page.url()).toBe(url);
  });

  test('explains a delete of an export that is already gone', async ({ page, exporter }) => {
    const entry = await exporter.runExport();
    const root = await openConfigPage(page);
    const row = root.locator('#exportHistory p').filter({ hasText: entry.fileName });
    await expect(row).toBeVisible();

    // Another administrator, or retention, removes the export while this page still lists it.
    await exporter.deleteExport(entry.id);
    await row.getByRole('button', { name: 'Delete' }).click();
    await expect(root.locator('#inventoryExporterError')).toHaveText(
      `Could not delete the export: Export ${entry.id} does not exist. Retention may have deleted it. Refresh the page to see the current exports.`,
    );
  });
});
