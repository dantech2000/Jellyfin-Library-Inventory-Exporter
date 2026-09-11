import { expect, test } from '../lib/fixtures';
import { ExportArchive } from '../lib/exports';

test.describe('scheduled task', () => {
  test('exports with the saved settings when it runs', async ({ adminApi, exporter }) => {
    await exporter.saveConfig({ ExportCsvByDefault: false, IncludeUserData: true });
    const task = (await adminApi.get<any[]>('/ScheduledTasks')).find(candidate => candidate.Key === 'LibraryInventoryExporter');
    expect(task).toMatchObject({ Name: 'Export library inventory', Category: 'Library Inventory Exporter' });

    await adminApi.post(`/ScheduledTasks/Running/${task.Id}`);
    await expect.poll(async () => (await exporter.exports()).length, { message: 'the task writes an export', timeout: 60_000 }).toBe(1);
    await expect
      .poll(async () => (await adminApi.get(`/ScheduledTasks/${task.Id}`)).LastExecutionResult?.Status, { timeout: 60_000 })
      .toBe('Completed');

    const [entry] = await exporter.exports();
    expect(entry).toMatchObject({ formats: ['json'], includesUserData: true });
    const archive = new ExportArchive(await exporter.download(entry.id));
    expect(archive.files).toEqual(['manifest.json']);
    expect(archive.manifest().exportOptions.includeUserData).toBe(true);
  });
});
