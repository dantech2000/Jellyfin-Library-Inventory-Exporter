#!/bin/bash
# Seeds a large media library for the performance suite (see perf.sh).
#
# Every media file is a hard link to one of three small template files, so tens of thousands of
# items take a few megabytes of disk. Jellyfin still probes every file, so exports see realistic
# media sources and streams. Sizes come from the PERF_* variables in e2e/perf/profiles/*.env.
set -euo pipefail

root="${1:-/media}"
ffmpeg_bin="${JELLYFIN_FFMPEG:-/usr/lib/jellyfin-ffmpeg/ffmpeg}"
movies="${PERF_MOVIES:-500}"
shows="${PERF_SHOWS:-50}"
seasons="${PERF_SEASONS:-2}"
episodes="${PERF_EPISODES:-10}"
albums="${PERF_ALBUMS:-100}"
tracks="${PERF_TRACKS:-10}"
seed_file="$root/.seed.json"
signature="$movies/$shows/$seasons/$episodes/$albums/$tracks"

if [ -f "$seed_file" ] && grep -q "\"signature\": \"$signature\"" "$seed_file"; then
    echo "Seeded library $signature already present in $root"
    exit 0
fi

rm -rf "$root/movies" "$root/shows" "$root/music" "$root/.templates" "$seed_file"
templates="$root/.templates"
mkdir -p "$templates"

ff() {
    "$ffmpeg_bin" -hide_banner -loglevel error -y "$@"
}

# Templates: a movie with two audio languages and an embedded subtitle, an episode, and an untagged
# song, so Jellyfin names songs and albums after their files and folders.
cat > "$templates/subtitle.srt" <<'EOF'
1
00:00:00,000 --> 00:00:01,000
Seed subtitle
EOF
ff -f lavfi -i "testsrc2=size=320x240:rate=24:duration=2" \
    -f lavfi -i "sine=frequency=440:duration=2" \
    -f lavfi -i "sine=frequency=660:duration=2" \
    -i "$templates/subtitle.srt" \
    -map 0:v -map 1:a -map 2:a -map 3:s \
    -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -b:a 48k -c:s srt \
    -metadata:s:a:0 language=eng -metadata:s:a:1 language=spa -metadata:s:s:0 language=eng \
    "$templates/movie.mkv"
ff -f lavfi -i "testsrc2=size=320x240:rate=24:duration=1" \
    -f lavfi -i "sine=frequency=550:duration=1" \
    -map 0:v -map 1:a \
    -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -b:a 48k -metadata:s:a:0 language=eng \
    "$templates/episode.mkv"
ff -f lavfi -i "sine=frequency=523:duration=1" -map_metadata -1 -c:a libmp3lame -b:a 32k "$templates/track.mp3"

# Movies: one folder each, with a TMDb ID in the folder name.
mkdir -p "$root/movies"
for ((i = 1; i <= movies; i++)); do
    printf -v name 'Seed Movie %05d (%d) [tmdbid-%d]' "$i" $((1950 + i % 70)) $((500000 + i))
    mkdir "$root/movies/$name"
    ln "$templates/movie.mkv" "$root/movies/$name/$name.mkv"
done

# Shows: one template show folder, hard-link copied for every show.
show_template="$templates/show"
for ((s = 1; s <= seasons; s++)); do
    printf -v season_dir '%s/Season %02d' "$show_template" "$s"
    mkdir -p "$season_dir"
    for ((e = 1; e <= episodes; e++)); do
        printf -v episode '%s/S%02dE%02d.mkv' "$season_dir" "$s" "$e"
        ln "$templates/episode.mkv" "$episode"
    done
done
mkdir -p "$root/shows"
for ((i = 1; i <= shows; i++)); do
    printf -v name 'Seed Show %04d (%d) [tvdbid-%d]' "$i" $((1980 + i % 40)) $((700000 + i))
    cp -al "$show_template" "$root/shows/$name"
done

# Music: ten albums per artist folder, each a hard-link copy of one template album.
album_template="$templates/album"
mkdir -p "$album_template"
for ((t = 1; t <= tracks; t++)); do
    printf -v track '%s/%02d - Seed Track %02d.mp3' "$album_template" "$t" "$t"
    ln "$templates/track.mp3" "$track"
done
for ((i = 1; i <= albums; i++)); do
    printf -v artist_dir '%s/music/Seed Artist %04d' "$root" $(((i - 1) / 10 + 1))
    mkdir -p "$artist_dir"
    printf -v album_dir '%s/Seed Album %05d' "$artist_dir" "$i"
    cp -al "$album_template" "$album_dir"
done

cat > "$seed_file" <<EOF
{
  "signature": "$signature",
  "movies": $movies,
  "series": $shows,
  "seasons": $((shows * seasons)),
  "episodes": $((shows * seasons * episodes)),
  "albums": $albums,
  "tracks": $((albums * tracks))
}
EOF
echo "Seeded $((movies + shows * seasons * episodes + albums * tracks)) media files in ${SECONDS}s"
