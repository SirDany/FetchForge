# Contributing to FetchForge

Contributions are welcome.

## Development setup

1. Install the .NET 10 SDK.
2. Install `yt-dlp`, `ffmpeg`, and `ffprobe` if you intend to work on media processing.
3. Run the app with `dotnet run --project .\src\FetchForge\FetchForge.csproj`.

## Guidelines

- Keep the basic workflow simple; advanced options should not overwhelm first-time users.
- Prefer structured configuration and argument lists over shell command-string concatenation.
- Do not run user-supplied arguments through PowerShell or `cmd.exe`.
- Keep third-party license notices accurate.
- Describe manual testing and relevant environment details in pull requests.
