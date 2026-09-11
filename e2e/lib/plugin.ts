import { expect } from '@playwright/test';
import type { JellyfinClient } from './jellyfin';

export const PLUGIN_ID = '7184fe02-8e91-4fd2-9140-6d58d5e91f0a';
export const PLUGIN_NAME = 'Library Inventory Exporter';

// Mirrors PluginConfiguration defaults.
export const DEFAULT_CONFIG = {
  OutputDirectory: '',
  ExportCsvByDefault: true,
  ExportJsonByDefault: true,
  IncludeUserData: false,
  IncludeMediaStreams: true,
  IncludeProviderIds: true,
  IncludePeople: false,
  IncludeImages: false,
  IncludeCollections: true,
  IncludePlaylists: false,
  CompressOutput: true,
  RetentionCount: 10,
  RetentionDays: 90,
  AnonymizeUsers: false,
};

export type PluginConfig = typeof DEFAULT_CONFIG;

export type ExportHistoryEntry = {
  id: string;
  generatedAt: string;
  formats: string[];
  fileName: string;
  sizeBytes: number;
  itemCount: number;
  libraryCount: number;
  status: string;
  includesUserData: boolean;
};

export type ExportStatus = {
  isRunning: boolean;
  exportId: string | null;
  stage: string;
  progressPercent: number;
  processedItems: number;
  totalItems: number;
  errorMessage: string | null;
};

export type ExportRequest = {
  formats?: string[];
  libraryIds?: string[];
  includeUserData?: boolean;
  includeMediaStreams?: boolean;
  compressOutput?: boolean;
};

export const EXPORT_ID_PATTERN = /^\d{4}-\d{2}-\d{2}T\d{6}Z$/;

// Output directory on the Docker exports volume, which the Playwright container can read.
export const EXPORTS_VOLUME_DIRECTORY = '/exports/jellyfin-inventory';

/** Export IDs have one-second resolution. Call this between exports that must not share an ID. */
export function nextExportSecond(): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, 1_100));
}

/** Plugin responses use the server's JSON casing. Tests read them in camelCase. */
export function camelize<T>(value: unknown): T {
  if (Array.isArray(value)) {
    return value.map(entry => camelize(entry)) as T;
  }

  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value).map(([key, entry]) => [key.charAt(0).toLowerCase() + key.slice(1), camelize(entry)]),
    ) as T;
  }

  return value as T;
}

/** Typed access to the plugin's REST API (`/InventoryExporter/*`) and its configuration. */
export class InventoryExporterApi {
  constructor(readonly client: JellyfinClient) {}

  async status(): Promise<ExportStatus> {
    return camelize(await this.client.get('/InventoryExporter/Status'));
  }

  async libraries(): Promise<{ id: string; name: string; collectionType?: string }[]> {
    return camelize(await this.client.get('/InventoryExporter/Libraries'));
  }

  async outputDirectories(): Promise<{ path: string; label: string; isDefault: boolean; isCurrent: boolean; isWritable: boolean }[]> {
    return camelize(await this.client.get('/InventoryExporter/OutputDirectories'));
  }

  async exports(): Promise<ExportHistoryEntry[]> {
    return camelize(await this.client.get('/InventoryExporter/Exports'));
  }

  async deleteExport(id: string): Promise<void> {
    await this.client.delete(`/InventoryExporter/Exports/${id}`);
  }

  async startExport(body: ExportRequest = {}): Promise<{ exportId: string; status: string }> {
    return camelize(await this.client.post('/InventoryExporter/Export', body));
  }

  /** Starts an export through the API and waits until the server reports it completed. */
  async runExport(body: ExportRequest = {}): Promise<ExportHistoryEntry> {
    const { exportId } = await this.startExport(body);
    expect(exportId, 'export id returned by POST /InventoryExporter/Export').toMatch(EXPORT_ID_PATTERN);
    await this.waitForExport(exportId);
    const entry = (await this.exports()).find(candidate => candidate.id === exportId);
    expect(entry, `history entry for export ${exportId}`).toBeTruthy();
    return entry!;
  }

  async waitForExport(exportId: string): Promise<void> {
    await expect
      .poll(
        async () => {
          const status = await this.status();
          if (status.exportId !== exportId || status.isRunning) {
            return 'running';
          }

          return status.errorMessage ? `Failed: ${status.errorMessage}` : status.stage;
        },
        { message: `export ${exportId} completes`, timeout: 60_000 },
      )
      .toBe('Completed');
  }

  async download(id: string): Promise<Buffer> {
    const response = await this.client.fetch('GET', `/InventoryExporter/Exports/${id}/Download`);
    expect(response.status(), `GET /InventoryExporter/Exports/${id}/Download`).toBe(200);
    return response.body();
  }

  async config(): Promise<PluginConfig> {
    return this.client.get(`/Plugins/${PLUGIN_ID}/Configuration`);
  }

  async saveConfig(changes: Partial<PluginConfig>): Promise<void> {
    await this.client.post(`/Plugins/${PLUGIN_ID}/Configuration`, { ...(await this.config()), ...changes });
  }

  /** Restores the default settings and deletes every export, so each test starts from a clean plugin. */
  async reset(): Promise<void> {
    await expect.poll(async () => (await this.status()).isRunning, { message: 'no export is running', timeout: 60_000 }).toBe(false);
    await this.deleteAllExports();
    await this.client.post(`/Plugins/${PLUGIN_ID}/Configuration`, DEFAULT_CONFIG);
    await this.deleteAllExports();
  }

  private async deleteAllExports(): Promise<void> {
    for (const entry of await this.exports()) {
      await this.deleteExport(entry.id);
    }
  }
}
