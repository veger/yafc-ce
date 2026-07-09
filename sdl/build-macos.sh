#!/usr/bin/env bash
#
# Build self-contained SDL2 / SDL2_image / SDL2_ttf dylibs for macOS from
# pinned upstream source and copy them into Yafc/lib/osx-arm64 and
# Yafc/lib/osx (Intel).
#
# Why from source instead of copying Homebrew's dylibs: Homebrew builds bake
# absolute /opt/homebrew/... paths into their Mach-O load commands, so the
# bundled libraries fail to load on any Mac without those exact paths (no
# Homebrew, Nix, a clean machine).
#
# The libraries produced here depend only on macOS system frameworks and on
# each other via @loader_path, so they are fully portable. Shared configuration
# (versions, checksums, CMake options) lives in common.sh.
#
# By default a universal (arm64 + x86_64) binary is built on one runner and
# split with lipo, so a single macOS runner covers both Yafc/lib/osx-arm64 and
# Yafc/lib/osx. The result is validated by sdl/check-macho.py before copying.
#
# Dependencies: cmake, ninja, curl, shasum, install_name_tool, lipo, codesign, a
# C/C++ toolchain (Xcode command line tools). All present on GitHub's macOS
# runners.
#
# Usage: sdl/build-macos.sh   (override versions via the env vars in common.sh)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
. "$SCRIPT_DIR/common.sh"

# ---- Config ------------------------------------------------------------------
# Architectures to build. Two arches -> a universal build that is lipo-split.
OSX_ARCHS="${OSX_ARCHS:-arm64;x86_64}"
# Deployment target: match the oldest macOS Yafc supports.
export MACOSX_DEPLOYMENT_TARGET="${MACOSX_DEPLOYMENT_TARGET:-11.0}"

REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
WORK="$SCRIPT_DIR/.build-macos"
STAGE="$WORK/stage"          # where the final universal dylibs are assembled
PREFIX="$WORK/prefix"        # cmake --install destination
ARM64_DIR="$REPO_ROOT/Yafc/lib/osx-arm64"
INTEL_DIR="$REPO_ROOT/Yafc/lib/osx"

# Final filenames Yafc loads (see Yafc/YafcLib.cs GetOsxMappedLibraryName).
# A function rather than an associative array: macOS ships bash 3.2.
output_name() {
    case "$1" in
        SDL2)       echo "libSDL2.dylib" ;;
        SDL2_image) echo "libSDL2_image.dylib" ;;
        SDL2_ttf)   echo "libSDL2_ttf.dylib" ;;
        *) echo "unknown key: $1" >&2; return 1 ;;
    esac
}

# Dump everything interesting about a Mach-O file: architectures, install id,
# and the full dependency list. Used before/after relinking so a broken load
# command is obvious in the CI log.
dump_macho() { # label file
    [ -n "${VERBOSE:-}" ] || return 0
    local label="$1" file="$2"
    echo "----- $label: $file"
    if [ ! -f "$file" ]; then
        echo "    (missing)"
        return 0
    fi
    echo "    file:  $(file -b "$file")"
    echo "    archs: $(lipo -archs "$file" 2>/dev/null || echo 'n/a')"
    echo "    id:    $(otool -D "$file" 2>/dev/null | tail -n +2)"
    echo "    deps:"
    otool -L "$file" 2>/dev/null | tail -n +2 | sed 's/^/      /'
    echo "    codesign:"
    codesign -dvv "$file" 2>&1 | sed 's/^/      /' || true
}

# build_lib SRC_DIR  extra cmake args...
build_lib() {
    local src="$1"; shift
    local bld="$WORK/build-$(basename "$src")"
    cmake -S "$src" -B "$bld" -G Ninja \
    -DCMAKE_BUILD_TYPE=Release \
    "${CMAKE_COMPAT_ARGS[@]}" \
    -DCMAKE_OSX_ARCHITECTURES="$OSX_ARCHS" \
    -DCMAKE_INSTALL_PREFIX="$PREFIX" \
    -DCMAKE_PREFIX_PATH="$PREFIX" \
    -DBUILD_SHARED_LIBS=ON \
    "$@"
    cmake --build "$bld" ${CMAKE_BUILD_VERBOSE:+--verbose}
    cmake --install "$bld"
}

# ---- Clean workspace ---------------------------------------------------------
rm -rf "$WORK"
mkdir -p "$WORK" "$STAGE" "$PREFIX"

# ---- Build -------------------------------------------------------------------
fetch "$SDL2_URL" "$SDL2_SHA256" "$WORK/SDL2.tar.gz"
tar -C "$WORK" -xf "$WORK/SDL2.tar.gz"
log "Building SDL2 $SDL2_VERSION ($OSX_ARCHS)"
build_lib "$WORK/SDL2-$SDL2_VERSION" -DSDL_STATIC=OFF -DSDL_TEST=OFF

fetch "$SDL2_IMAGE_URL" "$SDL2_IMAGE_SHA256" "$WORK/SDL2_image.tar.gz"
tar -C "$WORK" -xf "$WORK/SDL2_image.tar.gz"
log "Building SDL2_image $SDL2_IMAGE_VERSION (stb backend, no external codecs)"
build_lib "$WORK/SDL2_image-$SDL2_IMAGE_VERSION" "${SDL_IMAGE_ARGS[@]}" -DSDL2IMAGE_BACKEND_IMAGEIO=OFF

fetch "$SDL2_TTF_URL" "$SDL2_TTF_SHA256" "$WORK/SDL2_ttf.tar.gz"
tar -C "$WORK" -xf "$WORK/SDL2_ttf.tar.gz"
log "Building SDL2_ttf $SDL2_TTF_VERSION (vendored FreeType + HarfBuzz)"
build_lib "$WORK/SDL2_ttf-$SDL2_TTF_VERSION" "${SDL_TTF_ARGS[@]}"

# ---- Stage: rename, rewrite install names to @loader_path --------------------
# `cmake --install` writes versioned dylibs (libSDL2-2.0.0.dylib) plus
# unversioned symlinks. Copy the real file under the exact name Yafc loads, then
# rewrite every non-system reference to @loader_path so the libraries find each
# other next to the executable regardless of where Yafc is installed.
log "Staging freshly built dylibs"
for key in SDL2 SDL2_image SDL2_ttf; do
    name="$(output_name "$key")"
    # cp dereferences the unversioned symlink and copies the real dylib content.
    cp "$PREFIX/lib/$name" "$STAGE/$name"
    dump_macho "as-built $key" "$STAGE/$name"
done

log "Rewriting install names to @loader_path"
for key in SDL2 SDL2_image SDL2_ttf; do
    name="$(output_name "$key")"
    file="$STAGE/$name"
    install_name_tool -id "@loader_path/$name" "$file"
    # Rewrite any dependency that isn't an OS path to its bundled @loader_path
    # name. @rpath is NOT skipped: SDL's CMake build references its own libraries
    # as @rpath/libSDL2-2.0.0.dylib, which would not resolve in our bundle (we
    # ship libSDL2.dylib with an @loader_path id and no rpath).
    while IFS= read -r dep; do
        case "$dep" in
            /usr/lib/*|/System/*|@loader_path/*) continue ;;
        esac
        case "$(basename "$dep")" in
            libSDL2-*.dylib|libSDL2.dylib)             newname="$(output_name SDL2)" ;;
            libSDL2_image-*.dylib|libSDL2_image.dylib) newname="$(output_name SDL2_image)" ;;
            libSDL2_ttf-*.dylib|libSDL2_ttf.dylib)     newname="$(output_name SDL2_ttf)" ;;
            *)
                echo "Unexpected external dependency in $file: $dep" >&2
                echo "The build is supposed to be self-contained; refusing to continue." >&2
                exit 1
            ;;
        esac
        echo "    $key: $dep -> @loader_path/$newname"
        install_name_tool -change "$dep" "@loader_path/$newname" "$file"
    done < <(otool -L "$file" | tail -n +2 | awk '{print $1}')
done

# Editing a Mach-O invalidates its (ad-hoc) code signature; re-sign so dyld on
# Apple Silicon will load it.
log "Re-signing (ad-hoc)"
for key in SDL2 SDL2_image SDL2_ttf; do
    name="$(output_name "$key")"
    codesign --force --sign - "$STAGE/$name"
    dump_macho "relinked+signed $key" "$STAGE/$name"
done

# ---- Verify before copying anything into the repo ----------------------------
log "Verifying self-containment (universal staged dylibs)"
python3 "$SCRIPT_DIR/check-macho.py" "$STAGE"

# ---- Split universal -> per-arch and copy into Yafc/lib ----------------------
copy_arch() { # lipo-arch  dest-dir
    local arch="$1" dest="$2"
    mkdir -p "$dest"
    for key in SDL2 SDL2_image SDL2_ttf; do
        local name; name="$(output_name "$key")"
        if lipo "$STAGE/$name" -verify_arch "$arch" 2>/dev/null; then
            lipo "$STAGE/$name" -thin "$arch" -output "$dest/$name"
        else
            cp "$STAGE/$name" "$dest/$name"   # already single-arch
        fi
        codesign --force --sign - "$dest/$name"
        dump_macho "$arch $key" "$dest/$name"
    done
    log "Wrote $arch dylibs to $dest"
    ls -la "$dest"
}

copy_arch arm64 "$ARM64_DIR"
copy_arch x86_64 "$INTEL_DIR"

# Final gate: re-run the verifier against exactly what landed in the repo.
log "Verifying self-containment (per-arch dylibs in Yafc/lib)"
python3 "$SCRIPT_DIR/check-macho.py" "$ARM64_DIR" "$INTEL_DIR"

log "Done. Built SDL2 $SDL2_VERSION, SDL2_image $SDL2_IMAGE_VERSION, SDL2_ttf $SDL2_TTF_VERSION."
