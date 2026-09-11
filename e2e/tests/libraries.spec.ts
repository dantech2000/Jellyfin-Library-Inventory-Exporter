import { expect, test } from '../lib/fixtures';
import { virtualFolders } from '../lib/jellyfin';
import { openConfigPage } from '../lib/ui';

test.describe('library picker', () => {
  test('lists every Jellyfin library by name', async ({ page, adminApi, exporter }) => {
    const folders = await virtualFolders(adminApi);
    const names = folders.map(folder => folder.name).sort((a, b) => a.localeCompare(b));
    expect(names).toEqual(['Movies', 'Music', 'Shows']);

    const apiLibraries = await exporter.libraries();
    expect(apiLibraries.map(library => library.name)).toEqual(names);
    expect(apiLibraries.map(library => library.id).sort()).toEqual(folders.map(folder => folder.itemId).sort());

    const root = await openConfigPage(page);
    await expect(root.locator('#libraryList .libraryOption')).toHaveText(names);
    const ids = await root.locator('#libraryList .chkLibrary').evaluateAll(inputs => inputs.map(input => input.getAttribute('data-library-id')));
    expect(ids.sort()).toEqual(folders.map(folder => folder.itemId).sort());
  });

  test('enables single libraries only when "All libraries" is off', async ({ page }) => {
    const root = await openConfigPage(page);
    const allLibraries = root.locator('#chkAllLibraries');
    const libraries = root.locator('#libraryList .chkLibrary');

    await expect(allLibraries).toBeChecked();
    await expect(libraries).toHaveCount(3);
    for (const library of await libraries.all()) {
      await expect(library).toBeDisabled();
    }

    await root.getByText('All libraries').click();
    await expect(allLibraries).not.toBeChecked();
    for (const library of await libraries.all()) {
      await expect(library).toBeEnabled();
      await expect(library).not.toBeChecked();
    }

    await root.locator('#libraryList .libraryOption').filter({ hasText: 'Movies' }).click();
    await root.getByText('All libraries').click();
    for (const library of await libraries.all()) {
      await expect(library).toBeDisabled();
      await expect(library).not.toBeChecked();
    }
  });
});
