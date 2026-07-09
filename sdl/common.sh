#!/usr/bin/env bash
#
# Shared configuration and helpers for the SDL build scripts. This file is meant
# to be sourced by build-macos.sh and build-windows.sh, not run directly.
#
# It is the single source of truth for the pinned upstream versions and their
# checksums, the download URLs, and the CMake options that are the same on every
# platform. Anything genuinely platform-specific stays in the per-platform
# script.
#
# Keep this compatible with bash 3.2: macOS still ships it, and the macOS build
# runs there. No associative arrays.

# ---- Pinned upstream versions ------------------------------------------------
# Bumping SDL == change the version AND its checksum here, then re-run the
# builds. sdl/update-versions.sh fetches these values for you; sdl/README.md
# explains why the checksum is a committed pin rather than something the build
# computes for itself.
SDL2_VERSION="${SDL2_VERSION:-2.32.10}"
SDL2_IMAGE_VERSION="${SDL2_IMAGE_VERSION:-2.8.12}"
SDL2_TTF_VERSION="${SDL2_TTF_VERSION:-2.24.0}"

SDL2_SHA256="${SDL2_SHA256:-5f5993c530f084535c65a6879e9b26ad441169b3e25d789d83287040a9ca5165}"
SDL2_IMAGE_SHA256="${SDL2_IMAGE_SHA256:-393f5efb50536ec13ca4f4affb69cc9966d3c3f969e6c5e701faddf9f9785381}"
SDL2_TTF_SHA256="${SDL2_TTF_SHA256:-0b2bf1e7b6568adbdbc9bb924643f79d9dedafe061fa1ed687d1d9ac4e453bfd}"

# Release tarball URLs (github.com/libsdl-org/*/releases).
SDL2_URL="https://github.com/libsdl-org/SDL/releases/download/release-${SDL2_VERSION}/SDL2-${SDL2_VERSION}.tar.gz"
SDL2_IMAGE_URL="https://github.com/libsdl-org/SDL_image/releases/download/release-${SDL2_IMAGE_VERSION}/SDL2_image-${SDL2_IMAGE_VERSION}.tar.gz"
SDL2_TTF_URL="https://github.com/libsdl-org/SDL_ttf/releases/download/release-${SDL2_TTF_VERSION}/SDL2_ttf-${SDL2_TTF_VERSION}.tar.gz"

# ---- CMake options shared by both platforms ----------------------------------
# CMake 4 rejects the pre-3.5 cmake_minimum_required that SDL_ttf's vendored
# FreeType still declares; restore the old policy floor.
CMAKE_COMPAT_ARGS=( -DCMAKE_POLICY_VERSION_MINIMUM=3.5 )

# SDL2_image: built-in stb_image backend for PNG + JPG (all Yafc needs), no
# external codec libraries. IMG_SavePNG still works via SDL_image's miniz
# encoder. The backend to disable is platform-specific (ImageIO on macOS, WIC on
# Windows) and appended by the caller.
SDL_IMAGE_ARGS=(
    -DSDL2IMAGE_SAMPLES=OFF
    -DSDL2IMAGE_DEPS_SHARED=OFF
    -DSDL2IMAGE_VENDORED=OFF
    -DSDL2IMAGE_BACKEND_STB=ON
    -DSDL2IMAGE_PNG=ON -DSDL2IMAGE_JPG=ON -DSDL2IMAGE_PNG_SAVE=ON
    -DSDL2IMAGE_AVIF=OFF -DSDL2IMAGE_JXL=OFF -DSDL2IMAGE_TIF=OFF -DSDL2IMAGE_WEBP=OFF
)

# SDL2_ttf: vendored, statically linking its own FreeType + HarfBuzz.
SDL_TTF_ARGS=(
    -DSDL2TTF_SAMPLES=OFF
    -DSDL2TTF_VENDORED=ON
    -DSDL2TTF_HARFBUZZ=ON
)

# ---- Helpers -----------------------------------------------------------------
# Heavy logging while we validate the pipeline; set SDL_BUILD_QUIET=1 to trim the
# per-stage dumps in the platform scripts.
if [ -n "${SDL_BUILD_QUIET:-}" ]; then VERBOSE=""; else VERBOSE=1; fi

log() { echo "==> $*"; }

# sha256 of a file, using whichever tool the platform provides (Linux has
# sha256sum, macOS has shasum).
sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d' ' -f1
    else
        shasum -a 256 "$1" | cut -d' ' -f1
    fi
}

# fetch URL SHA256 OUTFILE -- download (unless already present) and verify the
# checksum against the committed pin.
fetch() {
    local url="$1" sha="$2" out="$3"
    if [ ! -f "$out" ]; then
        log "Downloading $(basename "$out")"
        curl -fL -o "$out" "$url"
    else
        log "Using cached $(basename "$out")"
    fi
    # Committed checksums may carry stray whitespace when edited by hand.
    local expected="${sha// /}" actual
    actual="$(sha256_of "$out")"
    if [ "$expected" != "$actual" ]; then
        echo "Checksum mismatch for $out" >&2
        echo "  expected: $expected" >&2
        echo "  actual:   $actual" >&2
        echo "If you meant to change the version, update the checksum in sdl/common.sh" >&2
        echo "(sdl/update-versions.sh prints the new values)." >&2
        exit 1
    fi
    log "Checksum OK for $(basename "$out")"
}
