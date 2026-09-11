import { readFile } from 'node:fs/promises';
import { Download, expect, Locator, Page } from '@playwright/test';
import { ExportArchive } from './exports';
import type { Credentials } from './jellyfin';

export const CONFIG_PAGE_URL = '/web/#/configurationpage?name=LibraryInventoryExporter';

export async function signInThroughWebUi(page: Page, user: Credentials): Promise<void> {
  await page.goto('/web/');
  await page.waitForURL(/#\/login/, { timeout: 60_000 });

  // The manual form is hidden when the login page lists users as cards.
  const nameInput = page.locator('#txtManualName');
  await expect(nameInput.or(page.locator('.btnManual')).first()).toBeVisible();
  if (!(await nameInput.isVisible())) {
    await page.locator('.btnManual').click();
  }

  await nameInput.fill(user.name);
  await page.locator('#txtManualPassword').fill(user.password);
  await page.locator('.manualLoginForm .button-submit').click();
  await page.waitForURL(/#\/home/, { timeout: 60_000 });
}

export async function openConfigPage(page: Page): Promise<Locator> {
  await page.goto(CONFIG_PAGE_URL);
  return waitForConfigPage(page);
}

/** Waits until the plugin page has loaded its settings, output directory suggestions, and libraries. */
export async function waitForConfigPage(page: Page): Promise<Locator> {
  const root = page.locator('#LibraryInventoryExporterConfigPage');
  await expect(root).toBeVisible({ timeout: 30_000 });
  await expect(root.locator('#outputDirectoryOptions [data-output-directory]').first(), 'output directory suggestions load').toBeVisible();
  await expect(root.locator('#libraryList > *').first(), 'library list loads').toBeAttached();
  return root;
}

/** Clicks "Run Export Now" and waits until the page lists that export as finished. Returns the export id. */
export async function runExportFromPage(page: Page, root: Locator): Promise<string> {
  const response = page.waitForResponse(
    candidate => candidate.request().method() === 'POST' && new URL(candidate.url()).pathname.endsWith('/InventoryExporter/Export'),
  );
  await root.locator('#btnRunExport').click();
  const started = await response;
  expect(started.status(), 'POST /InventoryExporter/Export').toBe(200);
  const exportId: string = (await started.json()).ExportId;

  // The status line can still show the previous export, so wait for this export's history entry.
  await expect(root.locator('#exportHistory')).toContainText(`jellyfin-inventory-${exportId}.zip`, { timeout: 60_000 });
  await expect(root.locator('#exportStatus')).toHaveText('Completed 100%');
  return exportId;
}

export function toast(page: Page, text: string | RegExp): Locator {
  return page.locator('.toast').filter({ hasText: text });
}

/** Runs `action`, waits for the browser download it starts, and opens the export archive. */
export async function downloadArchive(page: Page, action: () => Promise<void>): Promise<{ download: Download; archive: ExportArchive }> {
  const downloadEvent = page.waitForEvent('download', { timeout: 30_000 });
  await action();
  const download = await downloadEvent;
  return { download, archive: new ExportArchive(await readFile(await download.path())) };
}
