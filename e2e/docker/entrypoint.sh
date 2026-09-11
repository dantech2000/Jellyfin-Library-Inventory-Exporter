#!/bin/sh
# Seeds the side-loaded plugin build into the config volume, then starts Jellyfin.
set -eu

plugin_dir=/config/plugins/LibraryInventoryExporter
rm -rf "$plugin_dir"
mkdir -p "$plugin_dir"
cp -R /opt/e2e-plugin/. "$plugin_dir/"

exec /jellyfin/jellyfin "$@"
