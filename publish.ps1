param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutputDir = "artifacts/publish"
)

$ErrorActionPreference = "Stop"

Write-Host "Publishing FetchForge ($Configuration, $Runtime) to $OutputDir..."
dotnet publish "$PSScriptRoot/src/FetchForge/FetchForge.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "$PSScriptRoot/$OutputDir"

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Publish complete: $PSScriptRoot/$OutputDir"
