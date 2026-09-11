import { request } from '@playwright/test';
import { expect, test } from '../lib/fixtures';
import { jellyfinUrl, line } from '../lib/env';
import { downloadArchive, openConfigPage } from '../lib/ui';

test.describe('downloads', () => {
  test('"Download Latest Export" downloads the newest archive', async ({ page, exporter }) => {
    const entry = await exporter.runExport();
    const root = await openConfigPage(page);

    const { download, archive } = await downloadArchive(page, () => root.locator('#btnDownloadLatest').click());
    expect(download.suggestedFilename()).toBe(entry.fileName);
    expect(archive.rows('items.csv')).toHaveLength(entry.itemCount);
  });

  test('accepts the access token in the ApiKey query parameter', async ({ exporter, sessions }) => {
    const entry = await exporter.runExport();
    const browserless = await request.newContext({ baseURL: jellyfinUrl });
    try {
      const withApiKey = await browserless.get(`/InventoryExporter/Exports/${entry.id}/Download`, { params: { ApiKey: sessions.admin.token } });
      expect(withApiKey.status()).toBe(200);
      expect(withApiKey.headers()['content-type']).toBe('application/zip');

      // Jellyfin 12.0 turns off legacy authorization, which includes the api_key parameter.
      const withLegacyKey = await browserless.get(`/InventoryExporter/Exports/${entry.id}/Download`, { params: { api_key: sessions.admin.token } });
      expect(withLegacyKey.status()).toBe(line.name === '12' ? 401 : 200);
    } finally {
      await browserless.dispose();
    }
  });

  test('answers 404 for exports that do not exist', async ({ adminApi }) => {
    expect((await adminApi.fetch('GET', '/InventoryExporter/Exports/Latest')).status()).toBe(404);
    expect((await adminApi.fetch('GET', '/InventoryExporter/Exports/2000-01-01T000000Z/Download')).status()).toBe(404);
    expect((await adminApi.fetch('DELETE', '/InventoryExporter/Exports/2000-01-01T000000Z')).status()).toBe(404);
  });

  test('answers 400 for malformed export IDs', async ({ adminApi }) => {
    expect((await adminApi.fetch('GET', '/InventoryExporter/Exports/not-an-export/Download')).status()).toBe(400);
    expect((await adminApi.fetch('DELETE', '/InventoryExporter/Exports/not-an-export')).status()).toBe(400);
  });
});
