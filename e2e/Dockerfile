# syntax=docker/dockerfile:1
# Images for the E2E environment (see compose.yaml). Build context: repository root.
#   publish   builds the plugin for both Jellyfin lines
#   packages  zips both builds and writes a local plugin catalog manifest
#   catalog   serves that catalog over HTTP
#   jellyfin  a Jellyfin server with one build side-loaded

ARG JELLYFIN_IMAGE=jellyfin/jellyfin:12.0

# The plugin is platform-neutral IL, so it builds on the build machine's platform even when
# the Jellyfin image targets another one (see compose.platform.yaml).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src
COPY Directory.Build.props ./
COPY Jellyfin.Plugin.LibraryInventoryExporter/Jellyfin.Plugin.LibraryInventoryExporter.csproj Jellyfin.Plugin.LibraryInventoryExporter/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore Jellyfin.Plugin.LibraryInventoryExporter/Jellyfin.Plugin.LibraryInventoryExporter.csproj
COPY assets/ assets/
COPY Jellyfin.Plugin.LibraryInventoryExporter/ Jellyfin.Plugin.LibraryInventoryExporter/
RUN --mount=type=cache,target=/root/.nuget/packages \
    for tfm in net9.0 net10.0; do \
        dotnet publish Jellyfin.Plugin.LibraryInventoryExporter/Jellyfin.Plugin.LibraryInventoryExporter.csproj \
            --configuration Release --framework "$tfm" --no-restore --output "/out/publish/$tfm" || exit 1; \
    done

FROM --platform=$BUILDPLATFORM python:3.13-alpine AS packages
WORKDIR /src
COPY build.yaml ./
COPY tools/ tools/
COPY --from=publish /out/publish /out/publish
RUN python tools/package_plugin.py /out/publish/net9.0 /out/catalog \
 && python tools/package_plugin.py /out/publish/net10.0 /out/catalog \
 && python tools/generate_manifest.py build.yaml /out/catalog/manifest.json --local /out/catalog --base-url http://catalog

FROM nginx:stable-alpine AS catalog
COPY --from=packages /out/catalog /usr/share/nginx/html

FROM ${JELLYFIN_IMAGE} AS jellyfin
ARG PLUGIN_TFM=net10.0
COPY --from=publish /out/publish/${PLUGIN_TFM} /opt/e2e-plugin
COPY --chmod=755 e2e/docker/entrypoint.sh /usr/local/bin/e2e-entrypoint.sh
ENTRYPOINT ["/usr/local/bin/e2e-entrypoint.sh"]

# Unit tests on both target frameworks, for machines without the .NET 9 runtime:
#   docker build --file e2e/Dockerfile --target unit-tests --progress plain .
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS dotnet9-runtime

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS unit-tests
COPY --from=dotnet9-runtime /usr/share/dotnet/shared /usr/share/dotnet/shared
WORKDIR /src
COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet test --configuration Release
