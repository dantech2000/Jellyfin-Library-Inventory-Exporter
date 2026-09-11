import { readFileSync } from 'node:fs';
import path from 'node:path';

// Jellyfin server lines under test. The plugin version revision encodes the line.
const LINES = {
  '10.11': { targetAbi: '10.11.0.0', revision: 10, serverVersionPrefix: '10.11.', hostPort: 8911, cleanHostPort: 8921 },
  '12': { targetAbi: '12.0.0.0', revision: 12, serverVersionPrefix: '12.0.', hostPort: 8912, cleanHostPort: 8922 },
} as const;

type LineName = keyof typeof LINES;

const lineName = (process.env.JELLYFIN_LINE ?? '12') as LineName;
if (!(lineName in LINES)) {
  throw new Error(`JELLYFIN_LINE must be one of: ${Object.keys(LINES).join(', ')}`);
}

export const line = { name: lineName, ...LINES[lineName] };

// Inside the compose network the services are reachable by name. From the host, use the published ports.
export const jellyfinUrl = process.env.JELLYFIN_URL ?? `http://localhost:${line.hostPort}`;
export const jellyfinCleanUrl = process.env.JELLYFIN_CLEAN_URL ?? `http://localhost:${line.cleanHostPort}`;

// The plugin repository URL as Jellyfin sees it (always inside the compose network).
export const catalogUrl = process.env.CATALOG_URL ?? 'http://catalog';

// The Jellyfin /exports volume, mounted read-only into the Playwright container. Unset on the host.
export const exportsDir = process.env.EXPORTS_DIR;

const buildProps = readFileSync(path.resolve(__dirname, '..', '..', 'Directory.Build.props'), 'utf8');
const pluginVersion = /<PluginVersion>([^<]+)<\/PluginVersion>/.exec(buildProps)?.[1];
if (!pluginVersion) {
  throw new Error('Could not read <PluginVersion> from Directory.Build.props');
}

export const expectedPluginVersion = `${pluginVersion}.${line.revision}`;

export const admin = { name: 'admin', password: 'e2e-admin-password' };
export const viewer = { name: 'viewer', password: 'e2e-viewer-password' };

export const authDir = path.resolve(__dirname, '..', '.auth', line.name);
export const sessionsFile = path.join(authDir, 'sessions.json');
export const adminStorageState = path.join(authDir, 'admin-storage.json');
