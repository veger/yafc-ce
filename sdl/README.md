# Building the bundled SDL2 libraries

Yafc bundles SDL2, SDL2_image and SDL2_ttf for Windows and macOS so users don't
have to install them by hand. These scripts (re)build those libraries from
pinned upstream source, the same way [`lua/`](../lua) builds `liblua52`.

Linux is intentionally not covered: Linux users install SDL from their distro's
packages (see [`Docs/LinuxOsxInstall.md`](../Docs/LinuxOsxInstall.md)).

## Why build from source?

Previously the macOS dylibs were copied straight out of a Homebrew install. A
Homebrew build bakes absolute `/opt/homebrew/...` paths into its Mach-O load
commands, so the bundled libraries only worked on a Mac that already had
Homebrew with the exact same packages. On a clean Mac (or Nix) Yafc failed to
launch — [issue #659](https://github.com/Yafc-CE/yafc-ce/issues/659). Building
from source lets us produce libraries that are fully self-contained and
reproducible from a known configuration ([issue #274](https://github.com/Yafc-CE/yafc-ce/issues/274)).

## Configuration

Both platforms use the same configuration so behaviour is consistent:

* **SDL2_image** uses the built-in **stb_image** backend for PNG and JPG (all
  Yafc needs — see `IMG_Init(IMG_INIT_PNG | IMG_INIT_JPG)` in `Yafc.UI`). No
  external codec libraries (libpng, libjpeg, libwebp, libavif, ...) are linked.
  `IMG_SavePNG` still works via SDL_image's built-in miniz encoder.
* **SDL2_ttf** is built **vendored**, statically linking its own FreeType and
  HarfBuzz.

The result is three libraries that depend only on the operating system and on
`libSDL2` — nothing else has to be shipped.

## Rebuilding

The easy way is the **Build native libraries** GitHub Actions workflow
(`.github/workflows/build-native-libs.yml`): run it from the Actions tab,
optionally overriding the versions, and it builds and verifies every platform.
Enable *Open a pull request* to have it commit the results back.

To build locally instead:

### macOS (`build-macos.sh`)

Runs on macOS. Builds a universal (arm64 + x86_64) binary and splits it into
`Yafc/lib/osx-arm64` and `Yafc/lib/osx`, rewriting install names to
`@loader_path` and ad-hoc code-signing so the libraries are relocatable.

```
sdl/build-macos.sh
```

Requires the Xcode command line tools plus `cmake` and `ninja`
(`brew install cmake ninja`).

### Windows (`build-windows.sh`)

Cross-compiles on Linux with MinGW-w64 into `Yafc/lib/windows`. MinGW matches
the toolchain the existing Windows DLLs were built with, and the C/C++ runtime
is linked statically so no `libgcc`/`libstdc++`/`libwinpthread` DLLs are needed.

```
sdl/build-windows.sh
```

Requires `mingw-w64`, `cmake` and `ninja`
(`sudo apt-get install mingw-w64 cmake ninja-build`).

These three DLLs replace the old dependency set — `libpng16-16.dll`,
`libjpeg-9.dll`, `zlib1.dll` and `libfreetype-6.dll` are now built in and can be
deleted. `lua52.dll` is unrelated and left alone.

## Layout

* `common.sh` — sourced by both build scripts; the single source of truth for
  the pinned versions, checksums, download URLs, and the CMake options shared by
  every platform.
* `build-macos.sh`, `build-windows.sh` — the platform-specific builds.
* `update-versions.sh` — prints the version + checksum lines to paste into
  `common.sh`.
* `check-macho.py` — the macOS self-containment verifier (also the CI gate).

## Bumping a version

The pinned `*_SHA256` in `common.sh` is a **supply-chain pin**, not just a
download check: it lets every later build prove it fetched exactly the source
that was reviewed, protecting against a re-tagged release or a tampered artifact.
That is why the build never computes its own checksum — doing so would only
verify a download against itself. A human reviews and commits the value instead.

1. Run `sdl/update-versions.sh` (optionally passing explicit versions) to fetch
   the latest release versions and their checksums.
2. Paste the printed lines into `common.sh` (and update the workflow input
   defaults if you want the new versions to be the workflow's defaults).
3. Re-run the builds (workflow or local scripts).
4. Commit the updated libraries.

## Verifying (`check-macho.py`)

`check-macho.py` parses the Mach-O load commands of the committed macOS dylibs
and fails if any of them references a path that may be absent on a clean macOS
(anything outside `/usr/lib`, `/System/Library`, or a `@loader_path`/`@rpath`
relative path). It needs no macOS tooling, so it runs in CI on Linux via the
**Check native libraries** workflow on every pull request that touches
`Yafc/lib/`.

```
python3 sdl/check-macho.py Yafc/lib/osx-arm64 Yafc/lib/osx
```
