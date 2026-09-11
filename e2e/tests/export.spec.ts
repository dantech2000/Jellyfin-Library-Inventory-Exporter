import { expect, test } from '../lib/fixtures';
import { line } from '../lib/env';
import { CSV_HEADERS, ExportArchive } from '../lib/exports';
import { libraryItemIds, normalizeId, virtualFolders } from '../lib/jellyfin';
import { downloadArchive, openConfigPage, toast } from '../lib/ui';

const ALL_FILES = ['items.csv', 'manifest.json', 'media_sources.csv', 'media_streams.csv', 'provider_ids.csv'];

test.describe('export', () => {
  test('runs from the plugin page and downloads a complete archive', async ({ page, exporter }) => {
    const root = await openConfigPage(page);
    await expect(root.locator('#exportHistory')).toHaveText('No exports yet.');

    await root.locator('#btnRunExport').click();
    await expect(toast(page, 'Library inventory export started.')).toBeVisible();
    await expect(toast(page, 'Library inventory export completed.')).toBeVisible({ timeout: 60_000 });
    await expect(root.locator('#exportProgressStage')).toHaveText('Completed');
    await expect(root.locator('#exportProgressPercent')).toHaveText('100%');
    await expect(root.locator('.exportProgressBar')).toHaveAttribute('aria-valuenow', '100');
    await expect(root.locator('#exportStatus')).toHaveText('Completed 100%');
    await expect(root.locator('#btnRunExport')).toBeEnabled();
    await expect(root.locator('#btnRunExport')).toHaveText('Run Export Now');

    const exports = await exporter.exports();
    expect(exports).toHaveLength(1);
    const [entry] = exports;
    expect(entry).toMatchObject({ formats: ['csv', 'json'], libraryCount: 3, status: 'completed', includesUserData: false });

    const historyRow = root.locator('#exportHistory p').filter({ hasText: entry.fileName });
    await expect(historyRow).toContainText(`${entry.itemCount} items`);
    const { download, archive } = await downloadArchive(page, () => historyRow.getByRole('link', { name: entry.fileName }).click());
    expect(download.suggestedFilename()).toBe(entry.fileName);
    expect(archive.files).toEqual(ALL_FILES);
    expect(archive.rows('items.csv')).toHaveLength(entry.itemCount);
  });

  test('shows live progress while an export runs', async ({ page }) => {
    const root = await openConfigPage(page);

    // Hold the first two status polls at a mid-export state, then let the real status through.
    let polls = 0;
    await page.route('**/InventoryExporter/Status', async route => {
      polls++;
      if (polls > 2) {
        await route.continue();
        return;
      }

      await route.fulfill({
        json: { IsRunning: true, ExportId: 'x', Stage: 'Scanning library Movies', ProgressPercent: 42.4, ProcessedItems: 2, TotalItems: 5, ErrorMessage: null },
      });
    });

    await root.locator('#btnRunExport').click();
    await expect(root.locator('#btnRunExport')).toBeDisabled();
    await expect(root.locator('#btnRunExport')).toHaveText('Export Running');
    await expect(root.locator('#exportProgressStage')).toHaveText('Scanning library Movies');
    await expect(root.locator('#exportProgressPercent')).toHaveText('42%');
    await expect(root.locator('#exportProgressDetails')).toHaveText('2 of 5 items processed');
    await expect(root.locator('.exportProgressBar')).toHaveAttribute('aria-valuenow', '42');

    await expect(root.locator('#exportProgressStage')).toHaveText('Completed', { timeout: 30_000 });
    await expect(root.locator('#btnRunExport')).toBeEnabled();
  });

  test('archive matches what Jellyfin reports for every library', async ({ adminApi, exporter, sessions }) => {
    const entry = await exporter.runExport();
    const archive = new ExportArchive(await exporter.download(entry.id));

    expect(archive.files).toEqual(ALL_FILES);
    for (const file of ['items.csv', 'media_sources.csv', 'media_streams.csv', 'provider_ids.csv'] as const) {
      expect(archive.header(file), `${file} header`).toBe(CSV_HEADERS[file]);
    }

    const manifest = archive.manifest();
    expect(manifest).toMatchObject({
      schemaVersion: '1.0',
      server: { name: 'Jellyfin' },
      exportOptions: { includeUserData: false, includeMediaStreams: true, includeProviderIds: true },
    });
    expect(manifest.server.version, 'server.version names the Jellyfin line').toMatch(new RegExp(`^${line.serverVersionPrefix.replaceAll('.', '\\.')}`));

    const items = archive.rows('items.csv');
    expect(items).toHaveLength(entry.itemCount);
    for (const folder of await virtualFolders(adminApi)) {
      const expected = await libraryItemIds(adminApi, sessions.admin.userId, folder.itemId);
      expect(expected.length, `${folder.name} has fixture items`).toBeGreaterThan(0);

      const csvIds = items.filter(item => item.library_id === folder.itemId).map(item => item.item_id).sort();
      expect(csvIds, `${folder.name} items in items.csv`).toEqual(expected);

      const library = manifest.libraries.find((candidate: any) => candidate.id === folder.itemId);
      expect(library, `${folder.name} in manifest.json`).toMatchObject({ name: folder.name, collectionType: folder.collectionType });
      expect(library.items.map((item: any) => item.id).sort(), `${folder.name} items in manifest.json`).toEqual(expected);
    }
  });

  test('exports metadata, provider IDs, and stream details of the fixture media', async ({ adminApi, exporter, sessions }) => {
    const entry = await exporter.runExport();
    const archive = new ExportArchive(await exporter.download(entry.id));
    const items = archive.rows('items.csv');
    const find = (type: string, name: string) => {
      const item = items.find(candidate => candidate.item_type === type && candidate.name === name);
      expect(item, `${type} "${name}" in items.csv`).toBeTruthy();
      return item!;
    };
    // Without an NFO file, Jellyfin 12.0 keeps the whole folder name, "Second Movie (2019) [tmdbid-900002]", as the title.
    const shortName = (name: string) => name.replace(/ \(\d{4}\).*$/, '');
    const nameOf = (id: string) => shortName(items.find(item => item.item_id === id)?.name ?? '');

    const movie = find('Movie', 'Fixture Movie');
    expect(movie).toMatchObject({ library_name: 'Movies', production_year: '2020', official_rating: 'PG' });
    expect(movie.path).toMatch(/Fixture Movie \(2020\) \[imdbid-tt0000001\]\.mkv$/);
    expect(movie.overview).toContain('Generated fixture');
    const secondMovie = items.find(item => item.item_type === 'Movie' && shortName(item.name) === 'Second Movie')!;
    expect(secondMovie, 'Movie "Second Movie" in items.csv').toBeTruthy();
    find('Series', 'Fixture Show');
    find('MusicAlbum', 'Fixture Album');
    find('Audio', 'Fixture Track');
    const episodes = items.filter(item => item.item_type === 'Episode');
    expect(episodes.map(episode => `S${episode.season_number}E${episode.episode_number}`).sort()).toEqual(['S1E1', 'S1E2']);
    expect(episodes.map(episode => episode.series_name)).toEqual(['Fixture Show', 'Fixture Show']);

    const providerIds = archive.rows('provider_ids.csv').map(row => `${nameOf(row.item_id)}|${row.provider.toLowerCase()}|${row.provider_id}`);
    expect(providerIds).toEqual(
      expect.arrayContaining(['Fixture Movie|imdb|tt0000001', 'Fixture Movie|tmdb|900001', 'Second Movie|tmdb|900002', 'Fixture Show|tvdb|900003']),
    );

    // Stream rows must match what Jellyfin probed for the movie.
    const streams = archive.rows('media_streams.csv');
    const apiMovie = await adminApi.get(`/Items/${movie.item_id}`, { userId: sessions.admin.userId });
    const exportedStreams = streams
      .filter(stream => stream.item_id === movie.item_id)
      .map(stream => `${stream.stream_type}|${stream.codec}|${stream.language}|${stream.is_external}`)
      .sort();
    const probedStreams = apiMovie.MediaStreams.map(
      (stream: any) => `${stream.Type}|${stream.Codec}|${stream.Language ?? ''}|${stream.IsExternal ? 'True' : 'False'}`,
    ).sort();
    expect(exportedStreams).toEqual(probedStreams);
    expect(exportedStreams.filter(stream => stream.startsWith('Audio|')).map(stream => stream.split('|')[2]).sort()).toEqual(['eng', 'spa']);
    expect(exportedStreams.filter(stream => stream.startsWith('Subtitle|')).map(stream => stream.split('|')[3]).sort()).toEqual(['False', 'True']);

    const secondSources = archive.rows('media_sources.csv').filter(source => source.item_id === secondMovie.item_id);
    expect(secondSources).toHaveLength(1);
    const apiSecond = await adminApi.get(`/Items/${secondMovie.item_id}`, { userId: sessions.admin.userId });
    expect(secondSources[0]).toMatchObject({
      media_source_id: normalizeId(apiSecond.MediaSources[0].Id),
      size_bytes: String(apiSecond.MediaSources[0].Size),
      width: '1280',
      height: '720',
    });
    // The export keeps the container as Jellyfin stores it ("mov,mp4,m4a,3gp,3g2,mj2"). The API shortens it to one name.
    expect(secondSources[0].container.split(',')).toContain(apiSecond.MediaSources[0].Container);
    const secondVideo = streams.find(stream => stream.item_id === secondMovie.item_id && stream.stream_type === 'Video');
    expect(secondVideo).toMatchObject({ codec: 'h264', width: '1280', height: '720' });

    // The JSON manifest carries the same item and stream data as the CSV files.
    const jsonMovie = archive
      .manifest()
      .libraries.flatMap((library: any) => library.items)
      .find((item: any) => item.id === movie.item_id);
    expect(normalizeId(jsonMovie.id)).toBe(movie.item_id);
    expect(jsonMovie.providerIds).toMatchObject({ Imdb: 'tt0000001' });
    expect(jsonMovie.mediaSources.flatMap((source: any) => source.streams)).toHaveLength(probedStreams.length);
  });
});
