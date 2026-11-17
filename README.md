# FetchForge

**Download. Transform. Done.**

FetchForge is an open-source desktop application that brings `yt-dlp` downloading and FFmpeg audio post-processing together in one interface. It keeps common downloads simple while exposing curated, typed option editors, searchable full-tool `--help` options, live command previews, and portable JSON profiles.

## Features

- **Simple download workflow:** Paste a single video or playlist URL, pick an output folder, select a profile, and start or cancel at any time (cancelling terminates active child process trees).
- **Hybrid option exposure for both tools:**
  - **Curated, typed option controls** (~30+ options across `yt-dlp` and `ffmpeg`) grouped by category with toggles, numeric bounds, dropdowns, descriptions, and per-option reset.
  - **Runtime capability badges** (`Supported`, `Unsupported`, `Unverified`, `Built-in`) checked against your installed `yt-dlp`, `ffmpeg`, and `ffprobe` builds.
  - **Searchable full option browser** parsed directly from `yt-dlp --help` and `ffmpeg -h full`, with one-click `+ Add` into extra arguments.
  - **Extra arguments** passed as structured `argv` lists (never via shell interpolation).
- **Double-encode protection:** When FFmpeg processing is enabled, `yt-dlp` fetches best audio without re-encoding (`-x` skipped) so FFmpeg performs one clean encode. Turning FFmpeg off switches `yt-dlp` back to direct extraction or raw media download.
- **Live command preview & validation:** Inspect the exact `yt-dlp` and `ffmpeg` commands in real time, with conflict warnings when an extra argument overrides a curated option and errors if reserved job switches (`-o`, `-P`, `--print`, `-i`, `-progress`, etc.) are specified.
- **Concurrent FFmpeg processing queue:** Configurable concurrency limit (default: 16), `ffprobe`-backed per-file percentage progress, EQ/loudness filter chains, and collision-safe output naming.
- **Portable profiles & persistent settings:** Schema v2 profiles covering both tools (`%AppData%\FetchForge\profiles\*.json`), automatic migration of legacy v1 `AudioProfile` / `Default.json` files, and persistent app settings (`%AppData%\FetchForge\settings.json`).
- **Per-file report & temp cleanup:** Detailed completion summary with copyable report and automatic cleanup of per-job temporary folders.

## Technology

- C# and **Avalonia UI** (`.NET 10`) with **CommunityToolkit.Mvvm**.
- `yt-dlp`, `ffmpeg`, and `ffprobe` invoked directly as external processes via `ProcessStartInfo.ArgumentList` (no shell execution).

## Requirements

- Windows 10 or Windows 11 (or .NET 10 supported desktop OS when running from source).
- .NET 10 SDK (for building from source).
- `yt-dlp`, `ffmpeg`, and `ffprobe` installed on `PATH` or configured in the **Settings & Tools** tab.

FetchForge does not bundle third-party media binaries. Install each tool from its official source.

## Run and test from source

Open a terminal in the repository root:

```powershell
dotnet test .\FetchForge.slnx
dotnet run --project .\src\FetchForge\FetchForge.csproj
```

## Packaging

To build a self-contained single-file Windows x64 executable under `artifacts\publish\`:

```powershell
.\publish.ps1
```

## Local data

User settings and portable profiles are stored under:

```text
%AppData%\FetchForge\
├── settings.json
└── profiles\
    └── *.json
```

Legacy v1 audio profile JSON files imported through the UI are automatically migrated to schema v2.

## Advanced arguments and precedence

Extra arguments are appended after curated options as literal argument entries (one per line), never executed through PowerShell or `cmd.exe`.

- If an extra argument overlaps a curated option (including common aliases like `-f` or `-acodec`), the command preview raises a warning indicating which curated option is overridden.
- Switches managed internally by FetchForge (`-o`, `--output`, `-P`, `--paths`, `--print`, `--progress-template`, `--newline`, `-i`, `-y`, `-n`, `-progress`) are reserved and blocked with a validation error if entered in extra arguments.

## Legal and responsible use

FetchForge is a tool for downloading and processing media. Users are responsible for complying with applicable copyright law, platform terms, and any permissions required for the content they download.

## Third-party software

FetchForge does not claim ownership of `yt-dlp`, FFmpeg, Avalonia, or CommunityToolkit.Mvvm. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Roadmap

- [x] Repository license and project documentation
- [x] Avalonia MVVM shell and portable v2 profile store with legacy migration
- [x] Runtime tool capability discovery (`yt-dlp --help`, `ffmpeg -h full`, `ffmpeg -encoders`)
- [x] Download pipeline with structured progress, playlist ordering, and process-tree cancellation
- [x] FFmpeg processing queue with concurrency limit and `ffprobe` progress
- [x] Hybrid option editor (curated controls + searchable `--help` list) and live command preview
- [x] Per-file process report, collision-safe saves, and automatic temporary-folder cleanup
- [x] Automated unit tests and self-contained Windows release packaging script

## License

FetchForge's original code is licensed under the MIT License. See [LICENSE](LICENSE).
