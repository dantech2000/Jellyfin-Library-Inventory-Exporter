import { readdirSync } from 'node:fs';
import path from 'node:path';
import { expect, test } from '../lib/fixtures';
import { exportsDir } from '../lib/env';
import { EXPORTS_VOLUME_DIRECTORY, ExportHistoryEntry, nextExportSecond } from '../lib/plugin';
import { openConfigPage } from '../lib/ui';

// Entries on the shared exports volume that belong to one export (files and directories).
function entriesOnDisk(exportId: string): string[] {
  const directory = path.join(exportsDir!, path.basename(EXPORTS_VOLUME_DIRECTORY));
  return readdirSync(directory)
    .filter(name => name.includes(exportId))
    .sort();
}

function expectedFiles(entry: ExportHistoryEntry): string[] {
  return [`jellyfin-inventory-${entry.id}.history.json`, entry.fileName].sort();
}

test.describe('export history and retention', () => {
  test('lists exports newest first and deletes one from the plugin page', async ({ page, exporter }) => {
    await exporter.saveConfig({ OutputDirectory: EXPORTS_VOLUME_DIRECTORY });
    const older = await exporter.runExport();
    await nextExportSecond();
    const newer = await exporter.runExport();

    const root = await openConfigPage(page);
    const rows = root.locator('#exportHistory p');
    await expect(rows).toHaveCount(2);
    await expect(rows.nth(0)).toContainText(newer.fileName);
    await expect(rows.nth(1)).toContainText(older.fileName);

    await rows.filter({ hasText: older.fileName }).getByRole('button', { name: 'Delete' }).click();
    await expect(rows).toHaveCount(1);
    await expect(rows.first()).toContainText(newer.fileName);
    expect((await exporter.exports()).map(entry => entry.id)).toEqual([newer.id]);

    if (exportsDir) {
      expect(entriesOnDisk(older.id), 'nothing of the deleted export stays on disk').toEqual([]);
      expect(entriesOnDisk(newer.id), 'an export leaves only its archive and history file').toEqual(expectedFiles(newer));
    }
  });

  test('keeps only as many exports as the retention count allows', async ({ exporter }) => {
    await exporter.saveConfig({ RetentionCount: 2 });
    const first = await exporter.runExport();
    await nextExportSecond();
    const second = await exporter.runExport();
    await nextExportSecond();
    const third = await exporter.runExport();

    expect((await exporter.exports()).map(entry => entry.id)).toEqual([third.id, second.id]);
    expect((await exporter.client.fetch('GET', `/InventoryExporter/Exports/${first.id}/Download`)).status()).toBe(404);
  });

  test('writes archives to the configured output directory', async ({ exporter }) => {
    test.skip(!exportsDir, 'The exports volume is mounted only in the Playwright container.');

    await exporter.saveConfig({ OutputDirectory: EXPORTS_VOLUME_DIRECTORY });
    const current = (await exporter.outputDirectories()).find(option => option.isCurrent);
    expect(current?.path).toBe(EXPORTS_VOLUME_DIRECTORY);

    const entry = await exporter.runExport();
    expect(entriesOnDisk(entry.id)).toEqual(expectedFiles(entry));
  });
});
