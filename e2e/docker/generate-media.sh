#!/bin/bash
# Writes a tiny media library for the E2E suite: 2 movies, 1 show with 2 episodes, 1 album.
# Runs inside the Jellyfin image, which ships jellyfin-ffmpeg. Provider IDs come from
# folder names and NFO files, so library scans need no internet metadata.
set -euo pipefail

root="${1:-/media}"
ffmpeg_bin="${JELLYFIN_FFMPEG:-/usr/lib/jellyfin-ffmpeg/ffmpeg}"
marker="$root/.fixtures-v1"

if [ -f "$marker" ]; then
    echo "Fixture media already present in $root"
    exit 0
fi

ff() {
    "$ffmpeg_bin" -hide_banner -loglevel error -y "$@"
}

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
cat > "$tmp/subtitle.srt" <<'EOF'
1
00:00:00,000 --> 00:00:02,000
Fixture subtitle
EOF

# Movie with two audio languages, an embedded subtitle, and an external subtitle.
movie="$root/movies/Fixture Movie (2020) [imdbid-tt0000001]"
mkdir -p "$movie"
ff -f lavfi -i "testsrc2=size=320x240:rate=24:duration=3" \
    -f lavfi -i "sine=frequency=440:duration=3" \
    -f lavfi -i "sine=frequency=660:duration=3" \
    -i "$tmp/subtitle.srt" \
    -map 0:v -map 1:a -map 2:a -map 3:s \
    -c:v libx264 -preset ultrafast -pix_fmt yuv420p \
    -c:a aac -b:a 64k -ac:a:0 2 -ac:a:1 1 -c:s srt \
    -metadata:s:a:0 language=eng -metadata:s:a:0 title=English \
    -metadata:s:a:1 language=spa -metadata:s:a:1 title=Spanish \
    -metadata:s:s:0 language=eng \
    -disposition:a:0 default -disposition:a:1 0 -disposition:s:0 0 \
    "$movie/Fixture Movie (2020) [imdbid-tt0000001].mkv"
cp "$tmp/subtitle.srt" "$movie/Fixture Movie (2020) [imdbid-tt0000001].en.srt"
cat > "$movie/movie.nfo" <<'EOF'
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<movie>
  <title>Fixture Movie</title>
  <year>2020</year>
  <mpaa>PG</mpaa>
  <plot>Generated fixture for the Library Inventory Exporter E2E suite.</plot>
  <uniqueid type="imdb" default="true">tt0000001</uniqueid>
  <uniqueid type="tmdb">900001</uniqueid>
</movie>
EOF

# 720p movie in an MP4 container.
second="$root/movies/Second Movie (2019) [tmdbid-900002]"
mkdir -p "$second"
ff -f lavfi -i "testsrc2=size=1280x720:rate=24:duration=2" \
    -f lavfi -i "sine=frequency=330:duration=2" \
    -map 0:v -map 1:a \
    -c:v libx264 -preset ultrafast -pix_fmt yuv420p \
    -c:a aac -b:a 64k -ac 2 -metadata:s:a:0 language=eng \
    -movflags +faststart \
    "$second/Second Movie (2019) [tmdbid-900002].mp4"

# Show with one season and two episodes.
show="$root/shows/Fixture Show (2021) [tvdbid-900003]"
mkdir -p "$show/Season 01"
cat > "$show/tvshow.nfo" <<'EOF'
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<tvshow>
  <title>Fixture Show</title>
  <year>2021</year>
  <uniqueid type="tvdb" default="true">900003</uniqueid>
</tvshow>
EOF
for episode in 1 2; do
    ff -f lavfi -i "testsrc2=size=320x240:rate=24:duration=2" \
        -f lavfi -i "sine=frequency=$((500 + episode * 100)):duration=2" \
        -map 0:v -map 1:a \
        -c:v libx264 -preset ultrafast -pix_fmt yuv420p \
        -c:a aac -b:a 64k -metadata:s:a:0 language=eng \
        "$show/Season 01/Fixture Show S01E0${episode}.mkv"
done

# Album with one tagged track.
album="$root/music/Fixture Artist/Fixture Album"
mkdir -p "$album"
ff -f lavfi -i "sine=frequency=523:duration=3" \
    -c:a libmp3lame -b:a 64k \
    -metadata artist="Fixture Artist" -metadata album_artist="Fixture Artist" \
    -metadata album="Fixture Album" -metadata title="Fixture Track" \
    -metadata track=1 -metadata date=2022 \
    "$album/01 - Fixture Track.mp3"

touch "$marker"
echo "Fixture media written to $root:"
find "$root" -type f ! -name '.fixtures-*' | sort
