import { expect, test } from '../lib/fixtures';
import { CSV_HEADERS, ExportArchive } from '../lib/exports';
import { normalizeId, virtualFolders } from '../lib/jellyfin';
import { nextExportSecond } from '../lib/plugin';
import { openConfigPage, runExportFromPage } from '../lib/ui';

test.describe('export options', () => {
  test('exports only the libraries picked on the plugin page', async ({ page, adminApi, exporter }) => {
    const movies = (await virtualFolders(adminApi)).find(folder => folder.name === 'Movies')!;
    const root = await openConfigPage(page);
    await root.getByText('All libraries').click();
    await root.locator('#libraryList .libraryOption').filter({ hasText: 'Movies' }).click();
    await expect(root.locator(`.chkLibrary[data-library-id="${movies.itemId}"]`)).toBeChecked();

    const exportId = await runExportFromPage(page, root);
    const entry = (await exporter.exports()).find(candidate => candidate.id === exportId)!;
    expect(entry.libraryCount).toBe(1);
    const archive = new ExportArchive(await exporter.download(entry.id));
    expect(archive.manifest().libraries.map((library: any) => library.name)).toEqual(['Movies']);
    expect([...new Set(archive.rows('items.csv').map(item => item.library_name))]).toEqual(['Movies']);
  });

  test('Run Export Now follows the saved format settings', async ({ page, exporter }) => {
    await exporter.saveConfig({ ExportCsvByDefault: false });
    const root = await openConfigPage(page);
    await expect(root.locator('#chkCsv')).not.toBeChecked();

    const exportId = await runExportFromPage(page, root);
    const entry = (await exporter.exports()).find(candidate => candidate.id === exportId)!;
    expect(entry.formats).toEqual(['json']);
    expect(new ExportArchive(await exporter.download(entry.id)).files).toEqual(['manifest.json']);
  });

  test('writes only the formats named in the request', async ({ exporter }) => {
    const json = await exporter.runExport({ formats: ['json'] });
    expect(json.formats).toEqual(['json']);
    expect(new ExportArchive(await exporter.download(json.id)).files).toEqual(['manifest.json']);

    await nextExportSecond();
    const csv = await exporter.runExport({ formats: ['csv'] });
    expect(csv.formats).toEqual(['csv']);
    expect(new ExportArchive(await exporter.download(csv.id)).files).toEqual(['items.csv', 'media_sources.csv', 'media_streams.csv', 'provider_ids.csv']);
  });

  test('adds the watch state of every user when asked', async ({ exporter, sessions }) => {
    const entry = await exporter.runExport({ includeUserData: true });
    expect(entry.includesUserData).toBe(true);

    const archive = new ExportArchive(await exporter.download(entry.id));
    expect(archive.files).toContain('user_data.csv');
    expect(archive.header('user_data.csv')).toBe(CSV_HEADERS['user_data.csv']);
    expect(archive.manifest().exportOptions.includeUserData).toBe(true);

    const movie = archive.rows('items.csv').find(item => item.name === 'Fixture Movie')!;
    const rows = archive.rows('user_data.csv').filter(row => row.item_id === movie.item_id);
    const adminRow = rows.find(row => row.user_name === 'admin');
    expect(adminRow).toMatchObject({ user_id: normalizeId(sessions.admin.userId), played: 'True', is_favorite: 'True' });
    expect(adminRow!.last_played_date).not.toBe('');
    expect(rows.find(row => row.user_name === 'viewer')).toMatchObject({ user_id: normalizeId(sessions.viewer.userId), played: 'False', is_favorite: 'False' });
  });

  test('leaves out media streams and provider IDs when they are off', async ({ exporter }) => {
    await exporter.saveConfig({ IncludeProviderIds: false });
    const entry = await exporter.runExport({ includeMediaStreams: false });

    const archive = new ExportArchive(await exporter.download(entry.id));
    expect(archive.rows('items.csv')).toHaveLength(entry.itemCount);
    expect(archive.header('media_streams.csv')).toBe(CSV_HEADERS['media_streams.csv']);
    expect(archive.rows('media_streams.csv')).toHaveLength(0);
    expect(archive.rows('provider_ids.csv')).toHaveLength(0);
    expect(archive.manifest().exportOptions).toMatchObject({ includeMediaStreams: false, includeProviderIds: false });
  });
});
