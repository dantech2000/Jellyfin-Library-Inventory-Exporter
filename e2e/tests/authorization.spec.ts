import { expect, test } from '../lib/fixtures';

const ROUTES = [
  ['GET', '/InventoryExporter/Libraries'],
  ['GET', '/InventoryExporter/OutputDirectories'],
  ['POST', '/InventoryExporter/Export'],
  ['GET', '/InventoryExporter/Exports'],
  ['GET', '/InventoryExporter/Exports/Latest'],
  ['GET', '/InventoryExporter/Exports/2026-01-01T000000Z/Download'],
  ['DELETE', '/InventoryExporter/Exports/2026-01-01T000000Z'],
  ['GET', '/InventoryExporter/Status'],
] as const;

test.describe('authorization', () => {
  for (const [method, route] of ROUTES) {
    test(`${method} ${route} is for administrators only`, async ({ anonymousApi, viewerApi }) => {
      const data = method === 'POST' ? {} : undefined;
      expect((await anonymousApi.fetch(method, route, { data })).status(), 'anonymous').toBe(401);
      expect((await viewerApi.fetch(method, route, { data })).status(), 'non-admin user').toBe(403);
    });
  }

  test('a rejected export request does not start an export', async ({ viewerApi, exporter }) => {
    expect((await viewerApi.fetch('POST', '/InventoryExporter/Export', { data: {} })).status()).toBe(403);
    expect((await exporter.status()).isRunning).toBe(false);
    expect(await exporter.exports()).toEqual([]);
  });
});
