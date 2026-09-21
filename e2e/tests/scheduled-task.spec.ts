import { expect, test } from '../lib/fixtures';
import { ExportArchive } from '../lib/exports';
import { openConfigPage } from '../lib/ui';

const TICKS_PER_MINUTE = 600_000_000;
const TICKS_PER_HOUR = 60 * TICKS_PER_MINUTE;

test.describe('scheduled task', () => {
  test('runs only on demand until an administrator adds a schedule', async ({ page, exporter }) => {
    const task = await exporter.scheduledTask();
    expect(task).toMatchObject({ Name: 'Export library inventory', Category: 'Library Inventory Exporter', Triggers: [] });

    const root = await openConfigPage(page);
    await expect(root.locator('#exportScheduleText')).toHaveText('Exports run only when you click Run Export Now.');
    await root.locator('#exportScheduleLink').click();
    await expect(page).toHaveURL(new RegExp(`dashboard/tasks/${task.Id}`));
    await expect(page.getByText('Export library inventory').first()).toBeVisible();
  });

  test('shows the schedule that an administrator set in Jellyfin', async ({ page, exporter, adminApi }) => {
    const task = await exporter.scheduledTask();
    await adminApi.post(`/ScheduledTasks/${task.Id}/Triggers`, [
      { Type: 'DailyTrigger', TimeOfDayTicks: 3 * TICKS_PER_HOUR },
      { Type: 'WeeklyTrigger', DayOfWeek: 'Sunday', TimeOfDayTicks: 2 * TICKS_PER_HOUR + 30 * TICKS_PER_MINUTE },
      { Type: 'IntervalTrigger', IntervalTicks: 12 * TICKS_PER_HOUR },
    ]);

    const root = await openConfigPage(page);
    await expect(root.locator('#exportScheduleText')).toHaveText('Every day at 03:00. Every Sunday at 02:30. Every 12 hours.');
  });

  test('exports with the saved settings when it runs', async ({ adminApi, exporter }) => {
    await exporter.saveConfig({ ExportCsvByDefault: false, IncludeUserData: true });
    const task = await exporter.scheduledTask();

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
