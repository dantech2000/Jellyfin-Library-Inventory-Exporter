import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { expect } from '@playwright/test';
import { line } from './env';
import type { JellyfinClient } from './jellyfin';
import type { ExportRequest, InventoryExporterApi } from './plugin';

export const perfProfile = process.env.PERF_PROFILE ?? 'small';
export const perfUsers = Number(process.env.PERF_USERS ?? '5');
const seedFile = process.env.PERF_SEED_FILE ?? '/media/.seed.json';
const resultsDir = path.join(process.env.PERF_RESULTS_DIR ?? path.resolve(__dirname, '..', 'perf-results'), line.name);
const resultsFile = path.join(resultsDir, `${perfProfile}.json`);

// Written by e2e/docker/seed-library.sh.
export type Seed = { signature: string; movies: number; series: number; seasons: number; episodes: number; albums: number; tracks: number };

export function readSeed(): Seed {
  return JSON.parse(readFileSync(seedFile, 'utf8'));
}

export function seededItems(seed: Seed): number {
  return seed.movies + seed.series + seed.seasons + seed.episodes + seed.albums + seed.tracks;
}

/** A path next to the result file of this line and profile, for example for a screenshot. */
export function resultPath(name: string): string {
  mkdirSync(resultsDir, { recursive: true });
  return path.join(resultsDir, `${perfProfile}-${name}`);
}

/** Reads, changes, and writes the result file of this line and profile. perf/summarize.py turns it into Markdown. */
export function updateResults(change: (results: Record<string, any>) => void): void {
  mkdirSync(resultsDir, { recursive: true });
  const results = existsSync(resultsFile) ? JSON.parse(readFileSync(resultsFile, 'utf8')) : {};
  change(results);
  writeFileSync(resultsFile, JSON.stringify(results, null, 2));
}

export function sleep(milliseconds: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

function round(value: number, digits = 1): number {
  const factor = 10 ** digits;
  return Math.round(value * factor) / factor;
}

export function percentiles(values: number[]): { p50: number; p95: number; max: number } {
  const sorted = [...values].sort((a, b) => a - b);
  const at = (fraction: number) => (sorted.length === 0 ? 0 : sorted[Math.min(sorted.length - 1, Math.floor(fraction * sorted.length))]);
  return { p50: round(at(0.5)), p95: round(at(0.95)), max: round(sorted.at(-1) ?? 0) };
}

type ProgressSample = { t: number; stage: string; percent: number; processed: number; total: number };

/** Summarizes the progress that the status endpoint reported: how often it moved, and whether it stayed consistent. */
export function analyzeProgress(samples: ProgressSample[]) {
  let monotonic = true;
  let countsConsistent = true;
  let updates = 0;
  let lastChange = 0;
  let longestPause = 0;
  let previous: ProgressSample | undefined;

  for (const sample of samples) {
    if (previous && sample.percent < previous.percent) {
      monotonic = false;
    }

    if (sample.total > 0 && sample.processed > sample.total) {
      countsConsistent = false;
    }

    if (!previous || sample.stage !== previous.stage || sample.percent !== previous.percent || sample.processed !== previous.processed) {
      updates++;
      longestPause = Math.max(longestPause, sample.t - lastChange);
      lastChange = sample.t;
    }

    previous = sample;
  }

  // Time between samples counts toward the stage of the earlier sample. Library names are dropped, so
  // "Scanning Movies (library 1 of 3)" and "Scanning Music (library 3 of 3)" both count as "Scanning".
  const stageSeconds: Record<string, number> = {};
  for (let index = 1; index < samples.length; index++) {
    const stage = samples[index - 1].stage.startsWith('Scanning') ? 'Scanning' : samples[index - 1].stage;
    stageSeconds[stage] = round((stageSeconds[stage] ?? 0) + (samples[index].t - samples[index - 1].t) / 1000, 2);
  }

  return {
    samples: samples.length,
    updates,
    monotonic,
    countsConsistent,
    maxGapSeconds: round(longestPause / 1000),
    stages: [...new Set(samples.map(sample => sample.stage))],
    stageSeconds,
  };
}

/**
 * Starts an export through the API, then samples its status and an unrelated Jellyfin endpoint every 250 ms
 * until it finishes. The second series shows whether the export slows the rest of the server down.
 */
export async function measureExport(exporter: InventoryExporterApi, name: string, request: ExportRequest) {
  const startedAt = Date.now();
  let begin = performance.now();
  const { exportId } = await exporter.startExport(request);
  const startLatencyMs = round(performance.now() - begin);
  const samples: ProgressSample[] = [];
  const statusLatency: number[] = [];
  const serverLatency: number[] = [];

  for (;;) {
    begin = performance.now();
    const status = await exporter.status();
    statusLatency.push(performance.now() - begin);
    samples.push({ t: Date.now() - startedAt, stage: status.stage, percent: status.progressPercent, processed: status.processedItems, total: status.totalItems });

    begin = performance.now();
    await exporter.client.get('/System/Info/Public');
    serverLatency.push(performance.now() - begin);

    if (status.exportId === exportId && !status.isRunning) {
      expect(status.errorMessage ?? null, `export "${name}" failed`).toBeNull();
      break;
    }

    await sleep(250);
  }

  const finishedAt = Date.now();
  const entry = (await exporter.exports()).find(candidate => candidate.id === exportId);
  expect(entry, `history entry for export ${exportId}`).toBeTruthy();
  const seconds = (finishedAt - startedAt) / 1000;
  return {
    name,
    request,
    exportId,
    startedAt,
    finishedAt,
    seconds: round(seconds, 2),
    itemCount: entry!.itemCount,
    itemsPerSecond: Math.round(entry!.itemCount / seconds),
    archiveBytes: entry!.sizeBytes,
    startLatencyMs,
    statusLatencyMs: percentiles(statusLatency),
    serverLatencyMs: percentiles(serverLatency),
    progress: analyzeProgress(samples),
  };
}

export type ExportMeasurement = Awaited<ReturnType<typeof measureExport>>;

export function recordExport(measurement: ExportMeasurement): void {
  updateResults(results => {
    results.exports = [...(results.exports ?? []).filter((run: ExportMeasurement) => run.name !== measurement.name), measurement];
  });
}

/** Scans every library and waits until the scan task finishes. Returns the scan time in seconds. */
export async function scanAndWait(client: JellyfinClient, userId: string, timeoutMinutes = 90): Promise<number> {
  const started = Date.now();
  await client.post('/Library/Refresh');
  let lastLog = 0;

  for (;;) {
    const task = (await client.get<any[]>('/ScheduledTasks')).find(candidate => candidate.Key === 'RefreshLibrary');
    const result = task?.LastExecutionResult;
    // Jellyfin writes seven fractional digits. Keep three so every JavaScript engine parses the time.
    const finishedAt = result ? Date.parse(String(result.EndTimeUtc).replace(/(\.\d{3})\d+/, '$1')) : 0;
    if (task?.State === 'Idle' && finishedAt >= started - 1_000) {
      expect(result.Status, 'library scan result').toBe('Completed');
      return round((Date.now() - started) / 1000);
    }

    if (Date.now() - started > timeoutMinutes * 60_000) {
      throw new Error(`The library scan did not finish within ${timeoutMinutes} minutes.`);
    }

    if (Date.now() - lastLog > 30_000) {
      const counts = await client.get('/Items/Counts', { userId });
      console.log(
        `Scanning: ${Math.round(task?.CurrentProgressPercentage ?? 0)}% after ${Math.round((Date.now() - started) / 1000)} s. ` +
          `${counts.MovieCount} movies, ${counts.EpisodeCount} episodes, ${counts.SongCount} songs so far.`,
      );
      lastLog = Date.now();
    }

    await sleep(5_000);
  }
}
