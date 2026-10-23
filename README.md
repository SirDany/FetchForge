# FetchForge

**Download. Transform. Done.**

FetchForge is an open-source Windows desktop application intended to bring `yt-dlp` downloading and FFmpeg post-processing together in one straightforward interface. It is designed to keep common actions simple while making advanced command-line options and reusable audio-processing profiles accessible.

> **Project status:** Initial application shell. The current starter includes the WPF UI, profile import/export, local settings, and dependency checks. The download and FFmpeg job pipeline is not implemented yet.

## Planned features

- Download audio from a single URL or playlist using `yt-dlp`.
- Process downloaded files with FFmpeg, including optional EQ and loudness filters.
- Simple default workflow, with advanced settings for the full `yt-dlp` and FFmpeg option sets.
- Named sound/EQ profiles with JSON import and export.
- Configurable FFmpeg concurrency (default: 16).
- Per-file progress, cancellation, logs, and a final process report.
- Temporary working directories cleaned up after a job finishes.
- No account, cloud service, or project setup required.

## Technology

- C# and WPF on .NET 10 for a native Windows desktop UI.
- `yt-dlp`, `ffmpeg`, and `ffprobe` are external command-line dependencies.
- No third-party UI package is required by this starter.

WPF is a mature XAML-based Windows UI framework with data binding, custom styling, and Visual Studio support. Microsoft currently recommends WinUI 3 for new native Windows apps; WPF is selected here to keep this project's initial implementation straightforward and easy to work on. See [Microsoft's Windows app framework guidance](https://learn.microsoft.com/en-us/windows/apps/whats-new/coming-to-windows).

## Requirements

- Windows 10 or Windows 11.
- .NET 10 SDK.
- `yt-dlp`, `ffmpeg`, and `ffprobe` installed and available on `PATH`.

The app will not bundle these tools in the initial design. Install each dependency from its official source and review its license and distribution terms if you later decide to bundle binaries.

## Run from source

Open a terminal in the repository root:

```powershell
dotnet --info
dotnet run --project .\src\FetchForge\FetchForge.csproj
```

Alternatively, open `src/FetchForge/FetchForge.csproj` in Visual Studio with the .NET desktop development workload installed.

## Local data

The application stores user-specific settings and profiles under:

```text
%AppData%\FetchForge\
```

Profiles are JSON files and are intended to be portable between installations.

## Advanced arguments

The planned advanced settings will pass additional arguments to the corresponding executable. These are command-line arguments, not PowerShell scripts. Avoid pasting untrusted commands or shell operators into these fields.

FetchForge should not silently override user-supplied options. Where an option conflicts with a built-in setting, the UI should explain which value takes precedence.

## Legal and responsible use

FetchForge is a tool for downloading and processing media. Users are responsible for complying with applicable copyright law, platform terms, and any permissions required for the content they download.

## Third-party software

FetchForge does not claim ownership of `yt-dlp` or FFmpeg. Their licenses and obligations are separate from the MIT license covering FetchForge's original code. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Roadmap

- [x] Repository license and project documentation
- [x] Initial WPF shell and profile import/export
- [x] Dependency validation
- [ ] Download pipeline with cancellation and progress
- [ ] FFmpeg processing queue and concurrency limit
- [ ] Full advanced-option editor and command preview
- [ ] Per-file report and temporary-folder cleanup
- [ ] Automated tests and release packaging

## License

FetchForge's original code is licensed under the MIT License. See [LICENSE](LICENSE).
