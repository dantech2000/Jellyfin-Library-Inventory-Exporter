import { randomUUID } from 'node:crypto';
import { APIRequestContext, APIResponse, expect, request } from '@playwright/test';

export type Credentials = { name: string; password: string };
export type Session = { token: string; deviceId: string; userId: string };
type Params = Record<string, string | number | boolean>;

/**
 * Small Jellyfin API client. It sends only the `Authorization: MediaBrowser ...` header,
 * because Jellyfin 12.0 disables the legacy X-Emby-* headers and the api_key parameter.
 */
export class JellyfinClient {
  private constructor(
    private readonly context: APIRequestContext,
    readonly baseUrl: string,
    readonly deviceId: string,
    readonly token?: string,
  ) {}

  static async connect(baseUrl: string, session?: Pick<Session, 'token' | 'deviceId'>): Promise<JellyfinClient> {
    const context = await request.newContext({ baseURL: baseUrl });
    return new JellyfinClient(context, baseUrl, session?.deviceId ?? `e2e-${randomUUID()}`, session?.token);
  }

  authorization(): string {
    const fields = `Client="Playwright", Device="E2E", DeviceId="${this.deviceId}", Version="1.0.0"`;
    return this.token ? `MediaBrowser ${fields}, Token="${this.token}"` : `MediaBrowser ${fields}`;
  }

  fetch(method: string, path: string, options: { params?: Params; data?: unknown } = {}): Promise<APIResponse> {
    return this.context.fetch(path, {
      method,
      headers: { Authorization: this.authorization() },
      params: options.params,
      data: options.data,
    });
  }

  async json<T = any>(method: string, path: string, options: { params?: Params; data?: unknown } = {}): Promise<T> {
    const response = await this.fetch(method, path, options);
    if (!response.ok()) {
      throw new Error(`${method} ${path} returned HTTP ${response.status()}: ${await response.text()}`);
    }

    const body = await response.text();
    return (body ? JSON.parse(body) : undefined) as T;
  }

  get<T = any>(path: string, params?: Params): Promise<T> {
    return this.json<T>('GET', path, { params });
  }

  post<T = any>(path: string, data?: unknown, params?: Params): Promise<T> {
    return this.json<T>('POST', path, { data, params });
  }

  delete<T = any>(path: string, params?: Params): Promise<T> {
    return this.json<T>('DELETE', path, { params });
  }

  dispose(): Promise<void> {
    return this.context.dispose();
  }
}

export async function waitForHealthy(baseUrl: string, timeout = 180_000): Promise<void> {
  const context = await request.newContext({ baseURL: baseUrl });
  try {
    await expect
      .poll(
        async () => {
          try {
            const response = await context.get('/health', { timeout: 5_000 });
            return response.ok() ? (await response.text()).trim() : `HTTP ${response.status()}`;
          } catch (error) {
            return (error as Error).message.split('\n')[0];
          }
        },
        { message: `${baseUrl}/health reports Healthy`, timeout, intervals: [1_000] },
      )
      .toBe('Healthy');
  } finally {
    await context.dispose();
  }
}

/** Completes the first-run wizard with the given admin account. Does nothing on a configured server. */
export async function completeStartupWizard(baseUrl: string, admin: Credentials): Promise<void> {
  const client = await JellyfinClient.connect(baseUrl);
  try {
    const info = await client.get('/System/Info/Public');
    if (info.StartupWizardCompleted) {
      return;
    }

    await client.get('/Startup/User');
    await client.post('/Startup/User', { Name: admin.name, Password: admin.password });
    await client.post('/Startup/Complete');
  } finally {
    await client.dispose();
  }
}

export async function signIn(baseUrl: string, user: Credentials): Promise<Session> {
  const client = await JellyfinClient.connect(baseUrl);
  try {
    const result = await client.post('/Users/AuthenticateByName', { Username: user.name, Pw: user.password });
    return { token: result.AccessToken, deviceId: client.deviceId, userId: result.User.Id };
  } finally {
    await client.dispose();
  }
}

export const LIBRARIES = [
  { name: 'Movies', collectionType: 'movies', path: '/media/movies' },
  { name: 'Shows', collectionType: 'tvshows', path: '/media/shows' },
  { name: 'Music', collectionType: 'music', path: '/media/music' },
] as const;

// The item types LibraryScanner exports.
export const SUPPORTED_ITEM_TYPES = ['Movie', 'Series', 'Season', 'Episode', 'MusicAlbum', 'Audio', 'Video', 'BoxSet'];

// Remote metadata and image providers stay off, so scans are fast and need no internet.
const OFFLINE_TYPE_OPTIONS = ['Movie', 'Series', 'Season', 'Episode', 'MusicAlbum', 'MusicArtist', 'Audio', 'BoxSet'].map(type => ({
  Type: type,
  MetadataFetchers: [],
  MetadataFetcherOrder: [],
  ImageFetchers: [],
  ImageFetcherOrder: [],
}));

export async function ensureLibraries(client: JellyfinClient): Promise<void> {
  const existing = new Set((await client.get<any[]>('/Library/VirtualFolders')).map(folder => folder.Name));
  for (const library of LIBRARIES) {
    if (existing.has(library.name)) {
      continue;
    }

    await client.post(
      '/Library/VirtualFolders',
      {
        LibraryOptions: {
          EnableRealtimeMonitor: false,
          EnableChapterImageExtraction: false,
          ExtractChapterImagesDuringLibraryScan: false,
          EnableTrickplayImageExtraction: false,
          ExtractTrickplayImagesDuringLibraryScan: false,
          SaveLocalMetadata: false,
          MetadataSavers: [],
          TypeOptions: OFFLINE_TYPE_OPTIONS,
        },
      },
      { name: library.name, collectionType: library.collectionType, paths: library.path, refreshLibrary: false },
    );
  }
}

/** Scans the libraries until Jellyfin has indexed every fixture item and the scan task is idle. */
export async function scanLibraries(client: JellyfinClient, userId: string): Promise<void> {
  const fixtureIndexed = async () => {
    const counts = await client.get('/Items/Counts', { userId });
    return counts.MovieCount >= 2 && counts.SeriesCount >= 1 && counts.EpisodeCount >= 2 && counts.AlbumCount >= 1 && counts.SongCount >= 1;
  };

  if (!(await fixtureIndexed())) {
    await client.post('/Library/Refresh');
  }

  await expect.poll(fixtureIndexed, { message: 'library scan indexes the fixture media', timeout: 300_000, intervals: [2_000] }).toBe(true);
  await expect
    .poll(async () => (await client.get<any[]>('/ScheduledTasks')).find(task => task.Key === 'RefreshLibrary')?.State, {
      message: 'library scan task finishes',
      timeout: 300_000,
      intervals: [2_000],
    })
    .toBe('Idle');
}

export async function ensureUser(client: JellyfinClient, user: Credentials): Promise<void> {
  const users = await client.get<any[]>('/Users');
  if (!users.some(existing => existing.Name === user.name)) {
    await client.post('/Users/New', { Name: user.name, Password: user.password });
  }
}

export async function findItem(client: JellyfinClient, userId: string, type: string, name: string): Promise<any> {
  const result = await client.get('/Items', { userId, recursive: true, includeItemTypes: type, fields: 'Path,ProviderIds' });
  const item = result.Items.find((candidate: any) => candidate.Name === name);
  if (!item) {
    throw new Error(`No ${type} named "${name}". Found: ${result.Items.map((candidate: any) => candidate.Name).join(', ')}`);
  }

  return item;
}

/** The IDs Jellyfin itself reports for the exportable items in one library: the export oracle. */
export async function libraryItemIds(client: JellyfinClient, userId: string, libraryId: string): Promise<string[]> {
  const result = await client.get('/Items', {
    userId,
    parentId: libraryId,
    recursive: true,
    includeItemTypes: SUPPORTED_ITEM_TYPES.join(','),
  });
  return result.Items.map((item: any) => normalizeId(item.Id)).sort();
}

export async function virtualFolders(client: JellyfinClient): Promise<{ name: string; itemId: string; collectionType?: string }[]> {
  const folders = await client.get<any[]>('/Library/VirtualFolders');
  return folders.map(folder => ({ name: folder.Name, itemId: normalizeId(folder.ItemId), collectionType: folder.CollectionType }));
}

export function normalizeId(id: string): string {
  return id.replaceAll('-', '').toLowerCase();
}
