import { expect, test } from '../lib/fixtures';
import { ExportArchive } from '../lib/exports';
import { SUPPORTED_ITEM_TYPES } from '../lib/jellyfin';
import { measureExport, readSeed, recordExport, resultPath, sleep } from '../lib/perf';
import { openConfigPage } from '../lib/ui';

// Exports of the seeded library. Each test measures one export and records it in perf-results.
test.describe('export performance', () => {
  test('exports the whole library', async ({ exporter, adminApi, sessions }) => {
    const indexed = await adminApi.get('/Items', {
      userId: sessions.admin.userId,
      recursive: true,
      includeItemTypes: SUPPORTED_ITEM_TYPES.join(','),
      limit: 0,
    });

    const run = await measureExport(exporter, 'All libraries, CSV + JSON', {});
    recordExport(run);

    expect(run.itemCount, 'the export holds every item that Jellyfin indexed').toBe(indexed.TotalRecordCount);
    expect(run.progress.monotonic, 'progress never goes backwards').toBe(true);
    expect(run.progress.countsConsistent, 'processed items never exceed the total').toBe(true);

    const archive = new ExportArchive(await exporter.download(run.exportId));
    expect(archive.rows('items.csv')).toHaveLength(run.itemCount);
    expect(archive.rows('media_streams.csv').length, 'probed streams of every movie').toBeGreaterThanOrEqual(readSeed().movies * 4);
  });

  test('exports the watch state of every user', async ({ exporter, adminApi }) => {
    const users = await adminApi.get<any[]>('/Users');
    const run = await measureExport(exporter, `All libraries with watch state of ${users.length} users`, { includeUserData: true });
    recordExport(run);

    expect(run.progress.monotonic, 'progress never goes backwards').toBe(true);
    expect(run.progress.countsConsistent, 'processed items never exceed the total').toBe(true);
    expect((await exporter.exports()).find(entry => entry.id === run.exportId)?.includesUserData).toBe(true);
  });

  test('the plugin page shows the export moving forward', async ({ page }) => {
    const root = await openConfigPage(page);
    const started = page.waitForResponse(
      response => response.request().method() === 'POST' && new URL(response.url()).pathname.endsWith('/InventoryExporter/Export'),
    );
    await root.locator('#btnRunExport').click();
    const exportId: string = (await (await started).json()).ExportId;

    // The actions area: error panel, buttons, progress bar, schedule, and recent exports.
    const actions = root.locator('.verticalSection');
    const percents = new Set<string>();
    const details: string[] = [];
    const began = Date.now();
    let capturedProgress = false;
    while (!(await root.locator('#exportHistory').innerText()).includes(`jellyfin-inventory-${exportId}.zip`)) {
      expect(Date.now() - began, 'the export finishes within 30 minutes').toBeLessThan(30 * 60_000);
      const percent = await root.locator('#exportProgressPercent').innerText();
      percents.add(percent);
      details.push(await root.locator('#exportProgressDetails').innerText());
      if (!capturedProgress && percent !== '0%' && percent !== '100%') {
        await actions.screenshot({ path: resultPath('progress.png') });
        capturedProgress = true;
      }

      await sleep(200);
    }

    await actions.screenshot({ path: resultPath('completed.png') });

    const seconds = (Date.now() - began) / 1000;
    const intermediate = [...percents].filter(percent => percent !== '0%' && percent !== '100%');
    console.log(`The page showed ${intermediate.length} intermediate values during a ${seconds.toFixed(1)} s export: ${[...percents].join(', ')}`);
    if (seconds > 6) {
      expect(intermediate.length, 'the progress bar moves during a long export').toBeGreaterThanOrEqual(2);
    }

    for (const detail of details) {
      const counts = /^([\d,]+) of ([\d,]+) items/.exec(detail);
      if (counts) {
        expect(Number(counts[1].replaceAll(',', '')), `"${detail}"`).toBeLessThanOrEqual(Number(counts[2].replaceAll(',', '')));
      }
    }
  });
});
