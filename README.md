# Jellyfin Library Inventory Exporter

Jellyfin server plugin for exporting a read-only inventory of configured media libraries to CSV and JSON.

## Features

- Enumerates Jellyfin libraries and supported media items.
- Exports stable item metadata, paths, provider IDs, media sources, codecs, audio/subtitle stream details, resolution, and optional user watch state.
- Writes relational CSV files plus a versioned JSON `manifest.json` inside a timestamped ZIP archive.
- Adds an admin plugin page for configuration, manual exports with live progress, recent exports, and downloads.
- Explains every failure on the plugin page: what went wrong and what to do about it.
- Adds the scheduled task `Export library inventory`.
- Applies count and age based retention only to plugin-created export files.
- Includes GitHub Actions for CI, release ZIP packaging, MD5 checksum generation, and GitHub Pages plugin manifest publishing.
- Includes a Docker + Playwright end-to-end suite that runs against real Jellyfin 10.11 and 12.0 servers.

The plugin does not modify or delete media files, metadata, or user data.

## Compatibility

Each release ships one build per Jellyfin server line:

| Jellyfin server | Plugin build | targetAbi | .NET |
| --- | --- | --- | --- |
| 10.11.x | `0.1.9.10` | `10.11.0.0` | `net9.0` |
| 12.0.x | `0.1.9.12` | `12.0.0.0` | `net10.0` |

The last part of the plugin version names the server line. Jellyfin's catalog installs the highest version whose targetAbi the server supports, so each server gets the build made for it.

Jellyfin 12.0 does not load plugins built for 10.11. Before you upgrade a server from 10.11 to 12.0, the Jellyfin release notes recommend that you remove repository plugins. Install them again after the upgrade.

## Install From Plugin Repository

The recommended install path is Jellyfin's plugin catalog using this repository manifest:

```text
https://raw.githubusercontent.com/dantech2000/Jellyfin-Library-Inventory-Exporter/gh-pages/manifest.json
```

In Jellyfin:

1. Open `Dashboard`.
2. Go to `Plugins`.
3. Open `Repositories`.
4. Click `Add`.
5. Enter a name such as `Library Inventory Exporter`.
6. Paste the repository URL above.
7. Save.
8. Open `Catalog`.
9. Find `Library Inventory Exporter` under `General`.
10. Install the latest compatible version.
11. Restart Jellyfin when prompted.

After restart, open `Dashboard` -> `Plugins` -> `Library Inventory Exporter` -> `Settings` to configure exports.

If you installed the plugin before version `0.1.0` metadata was updated, remove and re-add the repository URL, refresh the catalog, then reinstall the plugin to pick up the catalog image and repository metadata.

## Use

1. Set an output directory, or leave it blank to use Jellyfin's data directory under `inventory-exports`. The page suggests directories that the Jellyfin server can write to. It shows a warning if the saved directory is not writable.
2. Keep `CSV by default` and `JSON by default` enabled if you want both formats in every export. `Run Export Now` and the scheduled task both use these settings.
3. Leave `Include user watch state` disabled unless you explicitly need played/favorite/progress data.
4. Keep `All libraries` checked, or clear it and select the libraries to export.
5. Click `Save`.
6. Click `Run Export Now`. The progress bar follows the export, and a message tells you when it completes or why it failed.
7. Use `Download Latest Export` or the recent export links to download the ZIP archive.

## Scheduled Exports

Exports run only when you click `Run Export Now`, until you add a schedule. The plugin page shows the current schedule.

To export on a schedule:

1. On the plugin page, click `Change schedule`. You can also open `Dashboard` -> `Scheduled Tasks` -> `Export library inventory`.
2. Add a trigger: daily at a time, weekly on a day and time, every few hours, or when Jellyfin starts.

Scheduled exports use the saved settings of the plugin page, including the formats, user watch state, and output directory.

Jellyfin triggers do not accept cron expressions. For a cron schedule, create an API key in `Dashboard` -> `API Keys`, and let cron call the REST API:

```bash
# Every Monday at 04:00
0 4 * * 1 curl -fsS -X POST -H 'Authorization: MediaBrowser Token="<api key>"' http://<jellyfin>:8096/InventoryExporter/Export
```

Exports are read-only with respect to Jellyfin media, metadata, and user data. The plugin only writes export archives and history files under the configured output directory.

## Export Contents

Each export is a ZIP archive named `jellyfin-inventory-<timestamp>.zip`:

| File | Contents |
| --- | --- |
| `items.csv` | One row per movie, series, season, episode, album, song, video, and collection. |
| `media_sources.csv` | One row per media source: path, container, size, bitrate, and resolution. |
| `media_streams.csv` | One row per video, audio, and subtitle stream: codec, language, channels, and flags such as default, forced, and external. |
| `provider_ids.csv` | One row per metadata provider ID, for example IMDb or TMDb. |
| `user_data.csv` | Played state, favorite, play count, and progress for each user. Only when `Include user watch state` is enabled. |
| `manifest.json` | The same inventory as one versioned JSON document. |

The CSV files come with the CSV format and `manifest.json` with the JSON format.

## Export Speed

These times come from the performance suite on Jellyfin 12.0, in a container with four CPU cores. Your server will differ, but the export time stays close to linear in the number of items.

| Library | Export | With watch state | Archive | Archive with watch state |
| ---: | ---: | ---: | ---: | ---: |
| 2,750 items | 0.8 s | 0.8 s | 0.5 MB | 0.8 MB |
| 14,300 items | 3.2 s | 3.1 s | 3.0 MB | 4.8 MB |
| 49,500 items | 9.5 s | 9.2 s | 10.3 MB | 16.5 MB |

As a rule of thumb, expect 3,000 to 5,000 items per second on four cores. A small library sits at the low end, because writing and compressing the archive costs a fixed fraction of a second. Watch state adds almost nothing, because the export reads it for all users in one step per 500 items. The runs above covered 7 users for the small library and 12 users for the other two.

Jellyfin 10.11 exports at a similar rate. Its 14,300-item run took 5.1 seconds, in an emulated container that is slower than the native container of the table above.

An export is not a library scan. It reads what Jellyfin has already indexed and never probes a media file. Jellyfin needed 22 minutes to index the 49,500-item library that the plugin exports in 9.5 seconds.

Two other things to expect at any size:

- The export uses at most half of the CPU cores, and never more than four threads, so playback keeps its share. While the 49,500-item export ran, Jellyfin answered other requests in 10 ms at the 95th percentile.
- The Jellyfin process needs more memory while an export runs: about 60 MB for 2,750 items and about 220 MB for 49,500 items.

## REST API

The plugin page uses these endpoints, and you can call them from scripts. Every endpoint requires a Jellyfin administrator. Send the token in an `Authorization: MediaBrowser Token="<token>"` header, or as the `ApiKey` query parameter. Jellyfin 12.0 no longer accepts the legacy `api_key` parameter.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/InventoryExporter/Libraries` | Libraries that the export can include. |
| `GET` | `/InventoryExporter/OutputDirectories` | Suggested output directories and whether Jellyfin can write to them. |
| `POST` | `/InventoryExporter/Export` | Starts an export in the background and returns its `ExportId`. Optional body fields: `formats` (`csv`, `json`), `libraryIds`, `includeUserData`, `includeMediaStreams`. |
| `GET` | `/InventoryExporter/Status` | Progress of the current or last export. |
| `GET` | `/InventoryExporter/Exports` | Export history, newest first. |
| `GET` | `/InventoryExporter/Exports/Latest` | Downloads the newest export. |
| `GET` | `/InventoryExporter/Exports/{id}/Download` | Downloads one export. |
| `DELETE` | `/InventoryExporter/Exports/{id}` | Deletes one export. |

Errors return [problem details](https://www.rfc-editor.org/rfc/rfc9457) with a `detail` message, for example HTTP 400 for an output directory that Jellyfin cannot write to and HTTP 409 while another export runs.

## Troubleshooting

| Message on the plugin page | What to do |
| --- | --- |
| `Jellyfin cannot write to <directory>` | Select a suggested directory, or enter a directory that the Jellyfin server can write to, and click `Save`. In Docker, use the container path of a writable volume. |
| `Another export is running` | Wait until the running export completes, then start the export again. |
| `There is no export to download yet` | Run an export first. |
| `Export <id> does not exist` | Retention or another administrator deleted the export. Refresh the page. |
| `Library inventory export failed: <reason>` | Read the reason in the message. For more detail, search the Jellyfin log for `InventoryExportService`. |
| `Only Jellyfin administrators can use the Library Inventory Exporter` | Sign in with an administrator account. |

## Build

The plugin targets `net9.0` for Jellyfin 10.11 and `net10.0` for Jellyfin 12.0. The .NET 10 SDK builds both.

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

`dotnet test` runs the unit tests on both target frameworks, so it needs the .NET 9 and .NET 10 runtimes. If you do not have the .NET 9 runtime, run the unit tests in Docker. `--output type=cacheonly` runs them without keeping an image:

```bash
docker build --file e2e/Dockerfile --target unit-tests --output type=cacheonly --progress plain .
```

## End-to-End Tests

`e2e/` holds a Docker test environment and a Playwright suite. The Docker images build the plugin, so the host needs only Docker with Compose v2.

```bash
./e2e/run.sh 12       # Jellyfin 12.0
./e2e/run.sh 10.11    # Jellyfin 10.11
./e2e/run.sh all      # both lines, one after the other
```

Each run does these steps:

1. Build the plugin for both lines, package both zips, and write a local plugin catalog.
2. Generate a small media library with ffmpeg: two movies, one show with two episodes, and one album.
3. Start Jellyfin with the plugin side-loaded, a stock Jellyfin for the catalog test, and the catalog server.
4. Complete the startup wizard, add and scan the libraries, and create an admin user and a non-admin user.
5. Run the Playwright specs in `e2e/tests`.
6. Remove the containers and volumes.

The HTML report is in `e2e/playwright-report/<line>/index.html`. If a run fails, the server logs are in `docker-compose.log` in the same folder.

On a CPU with SME but no SVE, such as the Apple M4, .NET can crash with an illegal instruction in arm64 containers ([dotnet/runtime#122608](https://github.com/dotnet/runtime/issues/122608)). `run.sh` detects this and runs the Jellyfin containers as `linux/amd64` through Rosetta. Set `JELLYFIN_PLATFORM=native` to turn this off, or `JELLYFIN_PLATFORM=linux/amd64` to force it. For the same reason, add `--platform linux/amd64` to the `unit-tests` Docker command on an Apple M4.

To inspect the servers after a run, set `KEEP=1`:

```bash
KEEP=1 ./e2e/run.sh 12
```

Jellyfin 12.0 then stays up at http://localhost:8912 and Jellyfin 10.11 at http://localhost:8911. Log in as `admin` with the password `e2e-admin-password`.

While a stack runs, you can also run Playwright from the host, for example in UI mode:

```bash
cd e2e
npm install
npx playwright install chromium
JELLYFIN_LINE=12 npx playwright test --ui
```

On the host, the specs skip the checks that read the Docker exports volume.

## Performance Tests

`e2e/perf.sh` builds a simulated library, waits for Jellyfin to index it, and then measures exports against it. Each media file is a hard link to one of three small template files, so tens of thousands of items take a few megabytes of disk. Jellyfin still probes every file, so the exports see real media sources and streams. The numbers in [Export Speed](#export-speed) come from this suite.

```bash
./e2e/perf.sh 12 small      # about 2,750 items
./e2e/perf.sh 12 medium     # about 14,300 items
./e2e/perf.sh 10.11 large   # about 49,500 items
```

| Profile | Movies | Series | Episodes | Albums | Songs | Extra users | Jellyfin index time |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `small` | 500 | 50 | 1,000 | 100 | 1,000 | 5 | about 1 min |
| `medium` | 2,000 | 200 | 6,000 | 500 | 5,000 | 10 | about 4 min |
| `large` | 5,000 | 500 | 20,000 | 2,000 | 20,000 | 10 | about 22 min |

A run measures:

- The export time and items per second, with and without the watch state of every user.
- The peak memory of the Jellyfin process during each export.
- How fast Jellyfin answers other requests while an export runs.
- Whether the progress moves steadily, never goes backwards, and never counts more items than the total.

Indexing the seeded library takes most of the wall-clock time, not the export. To measure a code change without paying for it twice, keep the stack and reuse its database:

```bash
KEEP=1 ./e2e/perf.sh 12 large            # leaves the stack running afterwards
REUSE=1 KEEP=1 ./e2e/perf.sh 12 large    # rebuilds the plugin, keeps the index
```

`REUSE=1` rebuilds the plugin and restarts Jellyfin with it, but skips the scan. A second large run then takes about a minute instead of 25.

Measure on an idle machine. A Docker build or another container on the same host can double an export time and make two runs impossible to compare.

The results are in `e2e/perf-results/<line>/<profile>.md` and `<profile>.json`. The `Performance` workflow in GitHub Actions runs the same suite on demand. Choose a profile and a Jellyfin line when you start it.

## Install From Source

Publish the build for your Jellyfin server line:

```bash
# Jellyfin 12.0
dotnet publish ./Jellyfin.Plugin.LibraryInventoryExporter/Jellyfin.Plugin.LibraryInventoryExporter.csproj -c Release -f net10.0 -o ./dist/plugin

# Jellyfin 10.11
dotnet publish ./Jellyfin.Plugin.LibraryInventoryExporter/Jellyfin.Plugin.LibraryInventoryExporter.csproj -c Release -f net9.0 -o ./dist/plugin
```

Copy the contents of `dist/plugin` into a Jellyfin plugin directory, for example `/var/lib/jellyfin/plugins/LibraryInventoryExporter`, then restart Jellyfin.

## Repository Publishing Flow

Maintainers publish installable releases with a tag. The tag must match `<PluginVersion>` in `Directory.Build.props`:

```bash
git tag v0.1.9
git push origin v0.1.9
```

The release workflow builds the plugin for both server lines. It attaches `Jellyfin.Plugin.LibraryInventoryExporter_0.1.9.10.zip` (Jellyfin 10.11) and `Jellyfin.Plugin.LibraryInventoryExporter_0.1.9.12.zip` (Jellyfin 12.0) to the GitHub release, each with an `.md5` checksum, plus `build.yaml`. The release notes come from the `changelog` in `build.yaml`.

When the release is published, the manifest workflow writes `manifest.json` to the `gh-pages` branch. It adds one catalog version per zip and reads the version and targetAbi from the `meta.json` inside each zip. The raw GitHub URL above is the most direct Jellyfin repository URL. If GitHub Pages is enabled for the repository without a conflicting custom domain, the Pages URL can also be used.

## Contributing

Bug reports and pull requests are welcome. [CONTRIBUTING.md](CONTRIBUTING.md) explains how to report a problem, set up a development environment, run the tests, support a new Jellyfin line, and publish a release.

## License

The Library Inventory Exporter is free software under the [GNU General Public License v3.0](LICENSE), the license of the Jellyfin plugin template. You can use, study, change, and share it. Anything you distribute that is built from it must stay under the same license.
