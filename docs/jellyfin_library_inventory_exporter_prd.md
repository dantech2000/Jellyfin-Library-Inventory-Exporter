# Jellyfin Library Inventory Exporter — PRD

## Product Summary

**Product name:** Jellyfin Library Inventory Exporter  
**Repository name:** `jellyfin-plugin-library-inventory-exporter`  
**Plugin category:** General  
**Target platform:** Jellyfin Server plugin  
**Primary language:** C# / .NET  

The Jellyfin Library Inventory Exporter is a Jellyfin server plugin that exports a durable, human-readable, and machine-readable manifest of the media libraries known to a Jellyfin server.

The plugin is intended for library auditing, migration planning, disaster recovery, spreadsheet review, and historical inventory tracking.

It is **not** a media downloader, request tool, or Jellyfin backup replacement. It exports inventory metadata about existing libraries.

---

## Problem Statement

Jellyfin administrators do not currently have a simple built-in way to export a complete portable inventory of their media libraries to CSV and JSON.

Jellyfin backups protect server configuration and database state, but they are not optimized for:

- Rebuilding a lost media collection
- Sharing or reviewing a title list
- Auditing duplicate, missing, or problematic media
- Exporting provider IDs such as IMDb, TMDb, TVDb, MusicBrainz, etc.
- Tracking watched and favorite state
- Comparing library state over time
- Reviewing codecs, subtitle formats, audio languages, and containers

This plugin fills that gap by generating a stable inventory manifest.

---

## Goals

### MVP Goals

- Export all Jellyfin libraries to CSV.
- Export all Jellyfin libraries to JSON.
- Support Movies, Series, Seasons, Episodes, Music Albums, Audio tracks, and generic Videos where practical.
- Include core item metadata:
  - Library name
  - Item type
  - Title
  - Sort title
  - Year
  - Runtime
  - Date added
  - Path
  - Provider IDs
- Include media source metadata:
  - Container
  - File size
  - Bitrate
  - Video codec
  - Audio codecs
  - Subtitle codecs/formats
  - Width
  - Height
  - HDR/video range info if available
- Optionally include per-user data:
  - Played
  - Favorite
  - Play count
  - Last played date
  - Playback position ticks
- Allow manual export from a plugin admin page.
- Add a scheduled task for recurring exports.
- Save exports to a configurable output directory.
- Allow downloading latest and previous exports from the admin page.
- Provide export retention settings.
- Package releases so users can install the plugin from a GitHub-hosted Jellyfin plugin repository.

### Non-Goals for MVP

- Restoring Jellyfin database state
- Restoring or downloading media files
- Requesting missing media
- Modifying Jellyfin library items
- Deleting duplicate media
- Automatic media cleanup
- Cross-client UI features
- Full backup replacement
- External metadata scraping beyond what Jellyfin already knows

---

## Target Users

- Jellyfin server administrators
- Users with large or long-lived media libraries
- Users migrating Jellyfin to a new host/storage layout
- Users recovering from disk loss
- Users who want spreadsheet-based library audits
- Users who want scheduled media manifests
- Users who want JSON manifests for automation

---

## User Stories

### Admin Exports Entire Library

As a Jellyfin administrator, I want to click **Run Export Now** so that I can download a CSV/JSON inventory of my current libraries.

Acceptance criteria:

- Admin can trigger export from plugin page.
- Export completes without modifying media or metadata.
- Export files are written to the configured output directory.
- Admin can download the export from the plugin page.

---

### Admin Schedules Recurring Export

As a Jellyfin administrator, I want weekly inventory exports so that I have a recent record of my media collection.

Acceptance criteria:

- Plugin provides an `IScheduledTask` named `Export library inventory`.
- Task appears in Jellyfin Scheduled Tasks.
- Task uses plugin configuration for format, output directory, and retention.
- Task supports cancellation.

---

### Admin Exports User Watch State

As a Jellyfin administrator, I want to optionally include watched/favorite state so that I can preserve user progress information for audit or recovery purposes.

Acceptance criteria:

- User data export is disabled by default.
- Admin must explicitly enable it.
- Export includes user IDs or usernames depending on configuration.
- Export includes played, favorite, play count, last played, and playback position where available.

---

### Admin Audits Subtitle and Audio Data

As a Jellyfin administrator, I want subtitle and audio stream information so that I can find media that may cause transcoding or playback issues.

Acceptance criteria:

- Export includes audio codec, language, channel count, and stream index where available.
- Export includes subtitle codec/format, language, forced/default flags, external flag, and stream index where available.
- CSV output separates stream rows from item rows to avoid unreadable mega-rows.

---

## Product Surface

### Plugin Configuration Page

The plugin should expose a Jellyfin admin dashboard page.

Configuration fields:

```yaml
OutputDirectory: /config/data/inventory-exports
DefaultFormats:
  - csv
  - json
IncludeUserData: false
IncludeMediaStreams: true
IncludeProviderIds: true
IncludePeople: false
IncludeImages: false
IncludeCollections: true
IncludePlaylists: false
CompressOutput: true
RetentionCount: 10
RetentionDays: 90
```

### Admin Actions

The plugin page should support:

- Run export now
- Select export format:
  - CSV
  - JSON
  - Both
- Select libraries or export all libraries
- Include/exclude user data for this export
- Include/exclude media stream details
- Download latest export
- View export history
- Delete previous export
- Show last export status
- Show last export duration
- Show last export item count

### Scheduled Task

Task name:

```text
Export library inventory
```

Default schedule:

```text
Disabled or weekly by default
```

The task should:

- Use configured export settings
- Write output to configured directory
- Apply retention cleanup after successful export
- Report progress
- Support cancellation tokens

---

## Technical Approach

### Preferred Approach

Build this as a native Jellyfin server plugin using internal Jellyfin services rather than an external script calling the REST API.

Reasoning:

- Native install experience
- Scheduled task support
- Direct access to server services
- No API key required
- Better integration with admin UI
- Easier to add download endpoints

### Internal Jellyfin Services to Investigate/Use

Likely services:

```csharp
ILibraryManager
IUserManager
IUserDataManager
IDtoService
IServerApplicationPaths
ILogger<T>
```

Potential supporting services:

```csharp
IFileSystem
IJsonSerializer
IApplicationHost
ILocalizationManager
IConfigurationManager
```

Implementation should verify current Jellyfin plugin API signatures for the targeted Jellyfin version.

---

## REST/API Endpoints Exposed by Plugin

Use plugin controller endpoints for the admin page.

Suggested endpoints:

```http
POST   /InventoryExporter/Export
GET    /InventoryExporter/Exports
GET    /InventoryExporter/Exports/Latest
GET    /InventoryExporter/Exports/{id}/Download
DELETE /InventoryExporter/Exports/{id}
GET    /InventoryExporter/Status
```

### `POST /InventoryExporter/Export`

Starts a manual export.

Request body:

```json
{
  "formats": ["csv", "json"],
  "libraryIds": ["library-guid-1", "library-guid-2"],
  "includeUserData": false,
  "includeMediaStreams": true,
  "compressOutput": true
}
```

Response:

```json
{
  "exportId": "2026-05-16T000000Z",
  "status": "running"
}
```

### `GET /InventoryExporter/Exports`

Returns previous exports.

Response:

```json
[
  {
    "id": "2026-05-16T000000Z",
    "generatedAt": "2026-05-16T00:00:00Z",
    "formats": ["csv", "json"],
    "fileName": "jellyfin-inventory-2026-05-16T000000Z.zip",
    "sizeBytes": 1234567,
    "itemCount": 4321,
    "libraryCount": 4,
    "status": "completed"
  }
]
```

### `GET /InventoryExporter/Status`

Returns active export status.

Response:

```json
{
  "isRunning": true,
  "exportId": "2026-05-16T000000Z",
  "stage": "Scanning library Movies",
  "progressPercent": 35,
  "processedItems": 1200,
  "totalItems": 4300
}
```

---

## Export Schema

### CSV Output Strategy

Do not create one huge CSV with repeated stream columns. Use multiple relational CSV files.

Recommended CSV files:

```text
items.csv
media_sources.csv
media_streams.csv
provider_ids.csv
user_data.csv
collections.csv
playlists.csv
```

The final ZIP should look like:

```text
jellyfin-inventory-2026-05-16T000000Z.zip
├── manifest.json
├── items.csv
├── media_sources.csv
├── media_streams.csv
├── provider_ids.csv
├── user_data.csv
├── collections.csv
└── playlists.csv
```

### `items.csv`

Fields:

```csv
item_id,library_id,library_name,item_type,name,sort_name,original_title,series_name,season_name,season_number,episode_number,production_year,premiere_date,runtime_ticks,date_created,path,parent_id,series_id,season_id,official_rating,community_rating,critic_rating,overview
```

### `media_sources.csv`

Fields:

```csv
item_id,media_source_id,path,container,size_bytes,bitrate,video_type,width,height,video_range,video_range_type,is_remote,run_time_ticks
```

### `media_streams.csv`

Fields:

```csv
item_id,media_source_id,stream_index,stream_type,codec,codec_tag,language,title,display_title,is_default,is_forced,is_external,channels,channel_layout,sample_rate,bitrate,width,height,profile,level,pixel_format,aspect_ratio
```

### `provider_ids.csv`

Fields:

```csv
item_id,provider,provider_id
```

Examples:

```csv
item_id,provider,provider_id
abc123,Tmdb,603
abc123,Imdb,tt0133093
```

### `user_data.csv`

Fields:

```csv
item_id,user_id,user_name,played,is_favorite,play_count,last_played_date,playback_position_ticks
```

This file is only generated when `IncludeUserData` is enabled.

### `collections.csv`

Fields:

```csv
collection_id,collection_name,item_id,item_name,item_type
```

### `playlists.csv`

Fields:

```csv
playlist_id,playlist_name,item_id,item_name,item_type
```

---

## JSON Manifest Schema

JSON output should be versioned and stable. Avoid dumping raw Jellyfin DTOs directly as the primary schema.

Top-level structure:

```json
{
  "schemaVersion": "1.0",
  "generatedAt": "2026-05-16T00:00:00Z",
  "server": {
    "name": "Jellyfin",
    "version": "10.11.x"
  },
  "exportOptions": {
    "includeUserData": false,
    "includeMediaStreams": true,
    "includeProviderIds": true
  },
  "libraries": [
    {
      "id": "library-guid",
      "name": "Movies",
      "collectionType": "movies",
      "items": [
        {
          "id": "item-guid",
          "type": "Movie",
          "name": "The Matrix",
          "sortName": "Matrix",
          "productionYear": 1999,
          "path": "/media/movies/The Matrix (1999)/The Matrix.mkv",
          "providerIds": {
            "Tmdb": "603",
            "Imdb": "tt0133093"
          },
          "mediaSources": [
            {
              "id": "media-source-id",
              "path": "/media/movies/The Matrix (1999)/The Matrix.mkv",
              "container": "mkv",
              "sizeBytes": 1234567890,
              "bitrate": 8000000,
              "video": {
                "codec": "h264",
                "width": 1920,
                "height": 1080,
                "bitrate": 7000000,
                "range": "SDR"
              },
              "audio": [
                {
                  "index": 1,
                  "codec": "aac",
                  "language": "eng",
                  "channels": 2,
                  "default": true
                }
              ],
              "subtitles": [
                {
                  "index": 2,
                  "codec": "subrip",
                  "language": "eng",
                  "external": false,
                  "forced": false,
                  "default": false
                }
              ]
            }
          ],
          "userData": []
        }
      ]
    }
  ]
}
```

---

## Functional Requirements

### FR1: Export All Libraries

The plugin shall enumerate all configured Jellyfin libraries and export inventory records for supported item types.

Supported MVP item types:

- Movie
- Series
- Season
- Episode
- MusicAlbum
- Audio
- Video
- BoxSet / Collection where practical

---

### FR2: Export Selected Libraries

The plugin shall allow the admin to select one or more libraries for export.

Acceptance criteria:

- Admin can select all libraries.
- Admin can select specific libraries.
- Export includes only selected libraries.

---

### FR3: CSV Export

The plugin shall generate flat CSV files suitable for spreadsheet usage.

Acceptance criteria:

- CSV files open cleanly in Excel, LibreOffice, and Numbers.
- CSV output escapes commas, quotes, and newlines correctly.
- UTF-8 encoding is used.
- Multiple CSV files are produced for relational data.

---

### FR4: JSON Export

The plugin shall generate a single versioned JSON manifest.

Acceptance criteria:

- JSON has `schemaVersion`.
- JSON includes generation timestamp.
- JSON validates as well-formed JSON.
- JSON does not expose raw internal-only Jellyfin objects unnecessarily.

---

### FR5: ZIP Export Bundle

When multiple files are generated, the plugin shall package them into a ZIP file.

Acceptance criteria:

- ZIP includes all selected export formats.
- ZIP uses timestamped file names.
- ZIP can be downloaded from admin page.

---

### FR6: Optional User Data Export

The plugin shall optionally export user watch state.

Acceptance criteria:

- Disabled by default.
- Admin must explicitly enable it.
- Includes played, favorite, play count, last played date, and playback position where available.
- Only admins can download exports containing user data.

---

### FR7: Scheduled Export

The plugin shall expose a scheduled task.

Acceptance criteria:

- Task appears in Jellyfin Scheduled Tasks.
- Task can be run manually from Jellyfin Scheduled Tasks.
- Task uses configured export settings.
- Task supports progress and cancellation.

---

### FR8: Retention Policy

The plugin shall delete old exports according to configurable retention settings.

Acceptance criteria:

- Retention by count is supported.
- Retention by age in days is supported.
- Retention cleanup only deletes files created by this plugin.
- Retention cleanup runs after successful export.

---

### FR9: Download Endpoint

The plugin shall provide authenticated admin-only endpoints for downloading exports.

Acceptance criteria:

- Admin can download latest export.
- Admin can download historical exports.
- Non-admin users cannot download exports.

---

### FR10: Progress Reporting

Manual and scheduled exports should report progress.

Progress stages:

- Preparing export
- Enumerating libraries
- Scanning library
- Writing CSV
- Writing JSON
- Compressing archive
- Applying retention
- Completed
- Failed

---

## Non-Functional Requirements

### Performance

The plugin should support large libraries.

Requirements:

- Avoid loading all rows into memory for CSV exports.
- Stream CSV writes.
- Use cancellation tokens.
- Avoid expensive optional fields by default.
- Do not export People/Cast by default.
- Keep memory usage predictable.
- Log timing for major export stages.

### Safety

The plugin must be read-only with respect to Jellyfin library items.

Requirements:

- Never delete media files.
- Never modify Jellyfin metadata.
- Never edit user data.
- Only write files under the configured output directory.
- Validate that output directory is writable.
- Prevent path traversal in download/delete endpoints.
- Restrict admin endpoints to authorized admins.

### Privacy

Requirements:

- User data export disabled by default.
- Clearly label exports containing user watch state.
- Consider config option to anonymize user IDs/usernames.

### Compatibility

Requirements:

- Target a specific Jellyfin ABI/version.
- Pin supported Jellyfin versions in manifest.
- Document compatibility.
- Prefer current stable Jellyfin server version for MVP.
- Test plugin load/install against target Jellyfin Docker image.

---

## Proposed Project Structure

```text
Jellyfin.Plugin.LibraryInventoryExporter/
├── Jellyfin.Plugin.LibraryInventoryExporter.csproj
├── Plugin.cs
├── PluginConfiguration.cs
├── build.yaml
├── Configuration/
│   ├── configPage.html
│   └── configPage.js
├── Controllers/
│   └── InventoryExporterController.cs
├── ScheduledTasks/
│   └── ExportInventoryTask.cs
├── Services/
│   ├── InventoryExportService.cs
│   ├── LibraryScanner.cs
│   ├── CsvExportWriter.cs
│   ├── JsonExportWriter.cs
│   ├── ExportRetentionService.cs
│   └── ExportFileStore.cs
├── Models/
│   ├── ExportOptions.cs
│   ├── ExportStatus.cs
│   ├── ExportHistoryEntry.cs
│   ├── InventoryManifest.cs
│   ├── InventoryLibrary.cs
│   ├── InventoryItem.cs
│   ├── InventoryMediaSource.cs
│   ├── InventoryMediaStream.cs
│   ├── InventoryProviderId.cs
│   └── InventoryUserData.cs
└── Tests/
    ├── CsvExportWriterTests.cs
    ├── JsonExportWriterTests.cs
    └── ExportRetentionServiceTests.cs
```

---

## Core Services

### `LibraryScanner`

Responsible for reading Jellyfin libraries and inventory items.

Inputs:

```csharp
ExportOptions options
IProgress<double> progress
CancellationToken cancellationToken
```

Output:

```csharp
IAsyncEnumerable<InventoryItem>
```

Responsibilities:

- Enumerate libraries
- Filter selected libraries
- Enumerate supported item types
- Map Jellyfin item data into stable plugin models
- Include provider IDs
- Include media source and media stream information
- Optionally include user data

---

### `InventoryExportService`

Orchestrates export lifecycle.

Responsibilities:

- Generate export ID
- Resolve output paths
- Call scanner
- Write CSV files
- Write JSON manifest
- Create ZIP archive
- Save export history
- Apply retention policy
- Track status/progress
- Handle cancellation and failures

---

### `CsvExportWriter`

Writes relational CSV files.

Responsibilities:

- Write `items.csv`
- Write `media_sources.csv`
- Write `media_streams.csv`
- Write `provider_ids.csv`
- Write `user_data.csv` if enabled
- Escape values correctly
- Use UTF-8

---

### `JsonExportWriter`

Writes versioned JSON manifest.

Responsibilities:

- Write stable JSON schema
- Include schema version
- Include generation metadata
- Include libraries and items
- Include nested media sources/streams
- Optionally include user data

---

### `ExportFileStore`

Manages export files.

Responsibilities:

- Resolve configured output directory
- Ensure directory exists
- List previous exports
- Return latest export
- Validate export IDs
- Open file streams for download
- Delete specific export files

---

### `ExportRetentionService`

Deletes old exports according to retention config.

Responsibilities:

- Keep last N exports
- Delete exports older than N days
- Only delete plugin-created files
- Log deleted files

---

## Configuration Model

Example C# model:

```csharp
public class PluginConfiguration : BasePluginConfiguration
{
    public string OutputDirectory { get; set; } = string.Empty;
    public bool ExportCsvByDefault { get; set; } = true;
    public bool ExportJsonByDefault { get; set; } = true;
    public bool IncludeUserData { get; set; } = false;
    public bool IncludeMediaStreams { get; set; } = true;
    public bool IncludeProviderIds { get; set; } = true;
    public bool IncludePeople { get; set; } = false;
    public bool IncludeImages { get; set; } = false;
    public bool IncludeCollections { get; set; } = true;
    public bool IncludePlaylists { get; set; } = false;
    public bool CompressOutput { get; set; } = true;
    public int RetentionCount { get; set; } = 10;
    public int RetentionDays { get; set; } = 90;
    public bool AnonymizeUsers { get; set; } = false;
}
```

---

## Release and Installation Strategy

### Manual Install for Development

For development/testing, build the plugin and copy the compiled plugin files into the Jellyfin plugin directory.

Typical Linux path:

```text
/var/lib/jellyfin/plugins/
```

Docker setups may map this to:

```text
/config/plugins/
```

Restart Jellyfin after copying plugin files.

---

### Recommended User Install: GitHub-Hosted Plugin Repository

Users should be able to install from a Jellyfin plugin repository manifest hosted on GitHub Pages.

User flow:

```text
Dashboard → Plugins → Repositories → Add repository
```

Repository URL example:

```text
https://<github-owner>.github.io/jellyfin-plugin-library-inventory-exporter/manifest.json
```

Then:

```text
Dashboard → Plugins → Catalog → Library Inventory Exporter → Install → Restart Jellyfin
```

---

## Plugin Manifest Metadata

Create `build.yaml` at the repository root.

Example:

```yaml
---
name: "Library Inventory Exporter"
guid: "PUT-A-REAL-UUID-HERE"
version: "0.1.0.0"
targetAbi: "10.11.0.0"
framework: "net8.0"
owner: "<github-owner>"
overview: "Export Jellyfin library inventory to CSV and JSON."
description: >
  Export a readable inventory manifest of Jellyfin libraries, media files,
  metadata provider IDs, media streams, and optional user watch state.
category: "General"
artifacts:
  - "Jellyfin.Plugin.LibraryInventoryExporter.dll"
changelog: >
  Initial preview release.
```

Replace:

- `guid` with a real UUID.
- `owner` with the GitHub owner/org.
- `targetAbi` with the supported Jellyfin ABI.
- `framework` with the framework expected by the target Jellyfin version.

---

## GitHub Actions

Use two workflows:

```text
.github/workflows/ci.yml
.github/workflows/release.yml
```

Optional third workflow:

```text
.github/workflows/publish-manifest.yml
```

---

## CI Workflow

`.github/workflows/ci.yml`

```yaml
name: CI

on:
  pull_request:
  push:
    branches:
      - main

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "9.0.x"

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --configuration Release --no-restore

      - name: Test
        run: dotnet test --configuration Release --no-build
```

Adjust `.NET` version once the target Jellyfin server/plugin SDK version is confirmed.

---

## Release Workflow

`.github/workflows/release.yml`

```yaml
name: Release

on:
  push:
    tags:
      - "v*.*.*"

permissions:
  contents: write

jobs:
  release:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "9.0.x"

      - name: Restore
        run: dotnet restore

      - name: Publish
        run: |
          dotnet publish ./Jellyfin.Plugin.LibraryInventoryExporter/Jellyfin.Plugin.LibraryInventoryExporter.csproj \
            -c Release \
            -o ./dist/plugin

      - name: Package
        run: |
          cd ./dist/plugin
          zip -r ../Jellyfin.Plugin.LibraryInventoryExporter.${GITHUB_REF_NAME}.zip .
          cd ..
          md5sum Jellyfin.Plugin.LibraryInventoryExporter.${GITHUB_REF_NAME}.zip \
            | awk '{ print $1 }' > Jellyfin.Plugin.LibraryInventoryExporter.${GITHUB_REF_NAME}.zip.md5

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          files: |
            dist/Jellyfin.Plugin.LibraryInventoryExporter.${{ github.ref_name }}.zip
            dist/Jellyfin.Plugin.LibraryInventoryExporter.${{ github.ref_name }}.zip.md5
            build.yaml
```

---

## Manifest Publishing Workflow

Option A: Generate the manifest manually in a script.

Option B: Use a Jellyfin plugin repo manifest generator GitHub Action.

Target output:

```text
gh-pages branch
└── manifest.json
```

Expected user-facing URL:

```text
https://<github-owner>.github.io/jellyfin-plugin-library-inventory-exporter/manifest.json
```

Suggested workflow shape:

```yaml
name: Publish Plugin Manifest

on:
  release:
    types: [published, edited, deleted]
  workflow_dispatch:

permissions:
  contents: write

jobs:
  manifest:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout gh-pages
        uses: actions/checkout@v4
        with:
          ref: gh-pages
          path: pages

      - name: Generate manifest
        run: |
          # TODO: generate pages/manifest.json from GitHub Releases.
          # This can be replaced by a Jellyfin plugin repository manifest action.
          echo '[]' > pages/manifest.json

      - name: Commit manifest
        run: |
          cd pages
          git config user.name "github-actions"
          git config user.email "github-actions@github.com"
          git add manifest.json
          git commit -m "Update plugin manifest" || exit 0
          git push
```

Codex should replace the placeholder manifest generation step with a real Jellyfin plugin manifest generator or a small script.

---

## MVP Milestones

### Milestone 0: Project Skeleton

Tasks:

- Create Jellyfin plugin project from plugin template.
- Rename namespace to `Jellyfin.Plugin.LibraryInventoryExporter`.
- Add plugin metadata.
- Add basic config page.
- Confirm plugin loads in Jellyfin.

Acceptance criteria:

- Plugin appears in Jellyfin plugin list.
- Plugin config page opens.
- Plugin logs startup message.

---

### Milestone 1: Basic JSON Export

Tasks:

- Inject library-related services.
- Enumerate libraries.
- Export basic item data:
  - ID
  - Type
  - Name
  - Year
  - Path
  - Library name
- Save JSON file to configured directory.

Acceptance criteria:

- Admin can trigger export manually.
- JSON file appears on disk.
- JSON contains expected library and item rows.

---

### Milestone 2: CSV Export

Tasks:

- Add CSV writer.
- Generate `items.csv`.
- Generate `provider_ids.csv`.
- Generate `media_sources.csv`.
- Generate `media_streams.csv`.

Acceptance criteria:

- CSV files open cleanly in spreadsheet software.
- CSV files contain expected values.
- Multiple media streams are represented as multiple rows.

---

### Milestone 3: Admin UI and Download Endpoints

Tasks:

- Add export history table.
- Add run button.
- Add latest export download button.
- Add previous export download/delete actions.
- Add status endpoint.

Acceptance criteria:

- Admin can run export from UI.
- Admin can see export history.
- Admin can download ZIP from UI.
- Admin can delete old exports.

---

### Milestone 4: Scheduled Task and Retention

Tasks:

- Add `IScheduledTask` implementation.
- Add progress reporting.
- Add cancellation support.
- Add retention cleanup.

Acceptance criteria:

- Task appears in Scheduled Tasks.
- Task can run manually or on schedule.
- Old exports are deleted according to config.

---

### Milestone 5: GitHub Release and Install Repository

Tasks:

- Add CI workflow.
- Add release workflow.
- Generate ZIP and MD5 assets.
- Generate Jellyfin plugin manifest.
- Publish manifest to GitHub Pages.
- Document install instructions.

Acceptance criteria:

- GitHub release contains plugin ZIP, MD5, and build metadata.
- Manifest URL is available from GitHub Pages.
- User can add repository URL in Jellyfin.
- Plugin appears in Jellyfin plugin catalog.

---

## Testing Plan

### Unit Tests

- CSV escaping
- JSON schema generation
- Export file naming
- Retention cleanup
- Path validation
- Export history parsing

### Integration Tests

- Plugin loads in target Jellyfin Docker image.
- Manual export works with sample media library.
- Scheduled task runs successfully.
- Download endpoint returns ZIP.
- Non-admin users cannot access admin endpoints.

### Manual Test Matrix

Libraries:

- Movies
- Shows
- Music
- Mixed videos

Media cases:

- Single audio stream
- Multiple audio streams
- Internal subtitles
- External subtitles
- Missing provider IDs
- 4K HDR media
- Episodes with season/episode numbers
- Items with missing paths or unavailable files

---

## Security Considerations

- Restrict all controller actions to admin users.
- Validate export IDs before resolving paths.
- Never allow arbitrary file download by path.
- Write only under configured output directory.
- Prevent path traversal attacks.
- Avoid leaking user data unless explicitly enabled.
- Consider an option to anonymize usernames/user IDs.

---

## Open Questions

1. Which Jellyfin version/ABI should MVP target?
2. Should MVP support Jellyfin 10.10, 10.11, or only the latest stable release?
3. Should user data identify users by username, ID, or anonymized identifier?
4. Should collections and playlists be included in MVP or v0.2?
5. Should exports include images/artwork paths?
6. Should JSON be one large file or newline-delimited JSON for very large libraries?
7. Should the plugin support output to remote destinations later, such as S3, SMB, or WebDAV?

---

## Future Enhancements

### v0.2

- Export collections
- Export playlists
- Export chapter data
- Export image/artwork paths
- Add anonymized user mode
- Add comparison between two exports

### v0.3

- Scheduled export notification via webhook
- ntfy/Gotify/Discord notification support
- S3-compatible backup target
- Export diff report:
  - Added items
  - Removed items
  - Changed paths
  - Changed provider IDs

### v0.4

- Library health audit mode:
  - Missing files
  - Duplicate items
  - Missing provider IDs
  - Missing artwork
  - Problematic subtitle formats
  - Old codecs

### v1.0

- Stable schema
- Documented API
- GitHub-hosted plugin repository
- Compatibility tested against current Jellyfin stable release
- Admin documentation

---

## Suggested First Codex Task

Build the initial project skeleton and basic export spike.

Prompt for Codex:

```text
Create a Jellyfin server plugin named Jellyfin.Plugin.LibraryInventoryExporter.

Start from the Jellyfin plugin template structure.

Implement:
- Plugin metadata
- PluginConfiguration with OutputDirectory and default export flags
- Basic admin config page
- InventoryExporterController with POST /InventoryExporter/Export
- InventoryExportService
- LibraryScanner that enumerates libraries and basic items using Jellyfin server services
- JsonExportWriter that writes a basic manifest JSON file
- ExportFileStore that writes to the configured output directory

For the first spike, only export:
- library id
- library name
- item id
- item type
- item name
- production year
- path if available

Do not implement CSV, scheduled tasks, user data, or GitHub Actions yet.

Keep the plugin read-only. Do not modify Jellyfin items, user data, metadata, or media files.
```
