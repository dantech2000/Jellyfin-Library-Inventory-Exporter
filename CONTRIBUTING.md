# Contributing

This guide covers how to report a problem, set up a development environment, run the tests, and publish a release.

## Report a Problem

Open an issue on GitHub and include:

- The Jellyfin version, shown in `Dashboard`.
- The plugin version, shown in `Dashboard` -> `Plugins`.
- The message that the plugin page shows.
- The related lines from the Jellyfin log (`Dashboard` -> `Logs`). Search for `Library Inventory Exporter` and `InventoryExport`.

## Development Setup

You need:

- The .NET 10 SDK. It builds both plugin targets.
- The .NET 9 runtime, to run the `net9.0` unit tests. Without it, run the unit tests in Docker (see [Unit Tests](#unit-tests)).
- Docker with Compose v2, for the end-to-end tests.

Then:

1. Clone the repository.
2. Run `dotnet build --configuration Release`.
3. Run `dotnet test --configuration Release`.

## Project Layout

| Path | Contents |
| --- | --- |
| `Jellyfin.Plugin.LibraryInventoryExporter/` | The plugin: REST controller, services, scheduled task, and plugin page (`Configuration/`). |
| `Jellyfin.Plugin.LibraryInventoryExporter.Tests/` | xUnit unit tests. They run on both target frameworks. |
| `e2e/` | Docker test environment and Playwright suite. |
| `tools/` | Scripts that package the plugin and write the plugin catalog manifest. |
| `Directory.Build.props` | Plugin version and the Jellyfin line that each target framework builds for. |

## Jellyfin Lines

Each target framework builds against one Jellyfin line:

| Target framework | Jellyfin packages | targetAbi | Version revision |
| --- | --- | --- | --- |
| `net9.0` | 10.11.x | `10.11.0.0` | `.10` |
| `net10.0` | 12.0.x | `12.0.0.0` | `.12` |

The code is shared. Use `#if NET10_0_OR_GREATER` only where a Jellyfin API differs between the lines.

To add a Jellyfin line:

1. In `Directory.Build.props`, add a `PropertyGroup` for the new target framework with `JellyfinVersion`, `JellyfinTargetAbi`, and `JellyfinAbiRevision`. The revision must be higher than the revisions of older lines.
2. Add the target framework to `<TargetFrameworks>` in both project files.
3. Add `e2e/env/<line>.env`, and add the line to `LINES` in `e2e/lib/env.ts`.
4. Add the line to the `e2e` matrix in `.github/workflows/ci.yml`, and the target framework to the publish loop in `.github/workflows/release.yml`.
5. Update the compatibility table in `README.md` and the tests in `PackageMetadataTests.cs`.

## Tests

### Unit Tests

```bash
dotnet test --configuration Release
```

This runs the tests on `net9.0` and `net10.0`. If you do not have the .NET 9 runtime, run them in Docker:

```bash
docker build --file e2e/Dockerfile --target unit-tests --progress plain .
```

On an Apple M4, add `--platform linux/amd64` to that command. .NET can crash with an illegal instruction in arm64 containers on that CPU ([dotnet/runtime#122608](https://github.com/dotnet/runtime/issues/122608)).

### End-to-End Tests

```bash
./e2e/run.sh all
```

The [End-to-End Tests](README.md#end-to-end-tests) section of the README explains what a run does and how to inspect the servers afterwards.

When you write a spec:

- Put it in `e2e/tests/<feature>.spec.ts`.
- Import `test` and `expect` from `e2e/lib/fixtures.ts`. Each test then starts with the default plugin settings and no exports.
- Compare export contents with what the Jellyfin API reports. Do not hardcode item counts, because Jellyfin versions index the same media differently.
- Run exports from the plugin page with `runExportFromPage()` in `e2e/lib/ui.ts`. It waits for the export that the click started, not for an earlier one.
- For each new failure path, add a case to `e2e/tests/errors.spec.ts` that checks the exact message on the plugin page.
- Run `npm run typecheck` in `e2e/` before you push.

## Code Guidelines

- Match the style of the surrounding code: file-scoped namespaces, braces on their own lines, and `ConfigureAwait(false)` on awaited tasks.
- The plugin reads Jellyfin data and never changes it. It writes only to its output directory.
- Every error that the plugin page can show must say what failed and what to do next. The controller returns problem details with that text in `detail`. The page shows it through `reportError()` in `configPage.js`.
- Jellyfin serializes the plugin API responses in PascalCase. The page converts them with `camelCaseKeys()`.

## Pull Requests

1. Create a branch from `main`.
2. Keep the change focused, and add or update tests for it.
3. Run the unit tests and `./e2e/run.sh all`.
4. Open a pull request. CI builds the plugin, runs the unit tests, and runs the end-to-end suite once for each Jellyfin line.

## Releases

`<PluginVersion>` in `Directory.Build.props` holds the release version, for example `0.1.8`. Each build appends the revision of its Jellyfin line, so version `0.1.8` ships as `0.1.8.10` for Jellyfin 10.11 and `0.1.8.12` for Jellyfin 12.0.

To publish a release:

1. Set `<PluginVersion>` in `Directory.Build.props` and `version` in `build.yaml` to the new version. A unit test checks that the two match.
2. Update `changelog` in `build.yaml` and in `Jellyfin.Plugin.LibraryInventoryExporter/meta.json`.
3. Merge the change into `main`, and wait until CI passes.
4. Tag the merge commit and push the tag:

   ```bash
   git tag v0.1.9
   git push origin v0.1.9
   ```

The release workflow stops if the tag does not match `<PluginVersion>`. Otherwise it builds both zips, attaches them with their `.md5` checksums to the GitHub release, and uses the `build.yaml` changelog as the release notes. The manifest workflow then updates `manifest.json` on the `gh-pages` branch.
