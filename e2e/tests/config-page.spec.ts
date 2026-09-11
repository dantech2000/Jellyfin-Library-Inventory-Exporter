import { expect, test } from '../lib/fixtures';
import { EXPORTS_VOLUME_DIRECTORY } from '../lib/plugin';
import { openConfigPage, toast, waitForConfigPage } from '../lib/ui';

test.describe('plugin settings page', () => {
  test('shows the default settings', async ({ page, exporter }) => {
    const defaultDirectory = (await exporter.outputDirectories()).find(option => option.isDefault);
    expect(defaultDirectory?.path).toMatch(/\/inventory-exports$/);

    const root = await openConfigPage(page);

    // An empty output directory falls back to the Jellyfin data directory suggestion.
    await expect(root.locator('#txtOutputDirectory')).toHaveValue(defaultDirectory!.path);
    await expect(root.locator('#chkCsv')).toBeChecked();
    await expect(root.locator('#chkJson')).toBeChecked();
    await expect(root.locator('#chkStreams')).toBeChecked();
    await expect(root.locator('#chkProviders')).toBeChecked();
    await expect(root.locator('#chkUserData')).not.toBeChecked();
    await expect(root.locator('#txtRetentionCount')).toHaveValue('10');
    await expect(root.locator('#txtRetentionDays')).toHaveValue('90');
  });

  test('suggests writable output directories, including the Docker volumes', async ({ page, exporter }) => {
    const options = await exporter.outputDirectories();
    expect(options.map(option => option.path)).toEqual(expect.arrayContaining(['/config/inventory-exports', EXPORTS_VOLUME_DIRECTORY]));

    const root = await openConfigPage(page);
    for (const option of options.filter(candidate => candidate.isWritable)) {
      await expect(root.locator(`[data-output-directory="${option.path}"]`)).toHaveText(`${option.label}: ${option.path}`);
    }

    await root.locator(`[data-output-directory="${EXPORTS_VOLUME_DIRECTORY}"]`).click();
    await expect(root.locator('#txtOutputDirectory')).toHaveValue(EXPORTS_VOLUME_DIRECTORY);
  });

  test('saves the settings and shows them again after a reload', async ({ page, exporter }) => {
    const root = await openConfigPage(page);
    await root.locator('#txtOutputDirectory').fill(EXPORTS_VOLUME_DIRECTORY);
    await root.getByText('CSV by default').click();
    await root.getByText('Include media streams').click();
    await root.getByText('Include user watch state').click();
    await root.locator('#txtRetentionCount').fill('3');
    await root.locator('#txtRetentionDays').fill('7');
    await root.getByRole('button', { name: 'Save' }).click();
    await expect(toast(page, /Settings saved/i)).toBeVisible();

    await expect
      .poll(() => exporter.config())
      .toMatchObject({
        OutputDirectory: EXPORTS_VOLUME_DIRECTORY,
        ExportCsvByDefault: false,
        ExportJsonByDefault: true,
        IncludeMediaStreams: false,
        IncludeProviderIds: true,
        IncludeUserData: true,
        RetentionCount: 3,
        RetentionDays: 7,
      });

    await page.reload();
    const reloaded = await waitForConfigPage(page);
    await expect(reloaded.locator('#txtOutputDirectory')).toHaveValue(EXPORTS_VOLUME_DIRECTORY);
    await expect(reloaded.locator('#chkCsv')).not.toBeChecked();
    await expect(reloaded.locator('#chkJson')).toBeChecked();
    await expect(reloaded.locator('#chkStreams')).not.toBeChecked();
    await expect(reloaded.locator('#chkProviders')).toBeChecked();
    await expect(reloaded.locator('#chkUserData')).toBeChecked();
    await expect(reloaded.locator('#txtRetentionCount')).toHaveValue('3');
    await expect(reloaded.locator('#txtRetentionDays')).toHaveValue('7');
  });
});
