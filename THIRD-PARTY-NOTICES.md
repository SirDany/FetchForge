# Third-party notices

FetchForge invokes external command-line tools installed by the user rather than bundling their executables, and uses open-source .NET libraries restored via NuGet.

## External Command-Line Tools (Not Bundled)

- **yt-dlp** — a separate project. Review its current license and the license notices for the exact executable/build you install: https://github.com/yt-dlp/yt-dlp
- **FFmpeg / ffprobe** — a separate project. Its license depends on the build configuration and enabled components: https://ffmpeg.org/legal.html

## NuGet Dependencies

- **Avalonia** (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`) — licensed under the MIT License: https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md
- **CommunityToolkit.Mvvm** — licensed under the MIT License: https://github.com/CommunityToolkit/dotnet/blob/main/License.md

The MIT license in this repository applies to FetchForge's original code, not to third-party dependencies. If the project later distributes binaries for `yt-dlp` or FFmpeg, review the precise build configuration, license texts, attribution, and corresponding-source obligations before publishing a release.
