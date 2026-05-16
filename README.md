# Jellyfin Library Inventory Exporter

Jellyfin server plugin for exporting a read-only inventory of configured media libraries to CSV and JSON.

## MVP

- Enumerates Jellyfin libraries and supported media items.
- Exports stable item metadata, paths, provider IDs, media sources, codecs, audio/subtitle stream details, resolution, and optional user watch state.
- Writes relational CSV files plus a versioned JSON `manifest.json` inside a timestamped ZIP archive.
- Adds an admin plugin page for configuration, manual exports, status, recent exports, and downloads.
- Adds the scheduled task `Export library inventory`.
- Applies count and age based retention only to plugin-created export files.
- Includes GitHub Actions for CI, release ZIP packaging, MD5 checksum generation, and GitHub Pages plugin manifest publishing.

The plugin does not modify or delete media files, metadata, or user data.

## Compatibility

The preview targets Jellyfin `10.11.x`, `targetAbi` `10.11.0.0`, and `net9.0`.

## Build

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

## Install From Source

```bash
dotnet publish ./Jellyfin.Plugin.LibraryInventoryExporter/Jellyfin.Plugin.LibraryInventoryExporter.csproj -c Release -o ./dist/plugin
```

Copy the contents of `dist/plugin` into a Jellyfin plugin directory, for example `/var/lib/jellyfin/plugins/LibraryInventoryExporter`, then restart Jellyfin.

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

After restart, open `Dashboard` -> `Plugins` -> `My Plugins` -> `Library Inventory Exporter` to configure exports.

## Use

1. Set an output directory, or leave it blank to use Jellyfin's data directory under `inventory-exports`.
2. Keep `CSV by default` and `JSON by default` enabled if you want both formats in every export.
3. Leave `Include user watch state` disabled unless you explicitly need played/favorite/progress data.
4. Click `Run Export Now`.
5. Use `Download Latest Export` or the recent export links to download the ZIP archive.

Exports are read-only with respect to Jellyfin media, metadata, and user data. The plugin only writes export archives and history files under the configured output directory.

If you installed the plugin before version `0.1.0` metadata was updated, remove and re-add the repository URL, refresh the catalog, then reinstall the plugin to pick up the catalog image and repository metadata.

## Repository Publishing Flow

Maintainers publish installable releases with a tag:

```bash
git tag v0.1.0
git push origin v0.1.0
```

The release workflow builds the plugin, creates `Jellyfin.Plugin.LibraryInventoryExporter.<tag>.zip`, generates an `.md5` checksum, and attaches both files plus `build.yaml` to the GitHub release.

When the release is published, the manifest workflow writes `manifest.json` to the `gh-pages` branch. The raw GitHub URL above is the most direct Jellyfin repository URL. If GitHub Pages is enabled for the repository without a conflicting custom domain, the Pages URL can also be used.
