#!/usr/bin/env bash
#
# Print the version + sha256 lines to paste into sdl/common.sh.
#
# The checksum committed in common.sh is a supply-chain pin: it lets every later
# build prove it fetched exactly the source that was reviewed, protecting against
# a re-tagged release or a tampered artifact. This helper only *discovers* the
# values (which is the tedious part); a human still reviews and commits them, so
# the pin keeps its meaning. That is why the build never computes its own
# checksum -- doing so would just be verifying a download against itself.
#
# Usage:
#   sdl/update-versions.sh                       # latest release of each library
#   sdl/update-versions.sh 2.32.10 2.8.12 2.24.0 # specific versions
#
# Dependencies: curl, and sha256sum or shasum.

set -euo pipefail

hasher() { if command -v sha256sum >/dev/null 2>&1; then sha256sum; else shasum -a 256; fi; }

# Latest stable release in the SDL2 (2.x) series for a libsdl-org repo. The repos
# now also publish SDL3 releases, which would show up as "latest" -- but Yafc
# uses SDL2, so restrict to release-2.x and ignore prereleases.
latest_version() { # repo
    curl -fsSL "https://api.github.com/repos/libsdl-org/$1/releases?per_page=100" \
    | grep -oE '"tag_name": *"release-2\.[0-9]+\.[0-9]+"' \
    | grep -oE '2\.[0-9]+\.[0-9]+' \
    | head -1
}

# sha256 of a release tarball, streamed (no temp file).
tarball_sha() { # url
    curl -fsSL "$1" | hasher | cut -d' ' -f1
}

SDL2_V="${1:-$(latest_version SDL)}"
IMG_V="${2:-$(latest_version SDL_image)}"
TTF_V="${3:-$(latest_version SDL_ttf)}"

sdl2_url="https://github.com/libsdl-org/SDL/releases/download/release-${SDL2_V}/SDL2-${SDL2_V}.tar.gz"
img_url="https://github.com/libsdl-org/SDL_image/releases/download/release-${IMG_V}/SDL2_image-${IMG_V}.tar.gz"
ttf_url="https://github.com/libsdl-org/SDL_ttf/releases/download/release-${TTF_V}/SDL2_ttf-${TTF_V}.tar.gz"

echo "# Paste these into sdl/common.sh, then re-run the builds:" >&2
echo
echo "SDL2_VERSION=\"\${SDL2_VERSION:-${SDL2_V}}\""
echo "SDL2_IMAGE_VERSION=\"\${SDL2_IMAGE_VERSION:-${IMG_V}}\""
echo "SDL2_TTF_VERSION=\"\${SDL2_TTF_VERSION:-${TTF_V}}\""
echo
echo "SDL2_SHA256=\"\${SDL2_SHA256:-$(tarball_sha "$sdl2_url")}\""
echo "SDL2_IMAGE_SHA256=\"\${SDL2_IMAGE_SHA256:-$(tarball_sha "$img_url")}\""
echo "SDL2_TTF_SHA256=\"\${SDL2_TTF_SHA256:-$(tarball_sha "$ttf_url")}\""
