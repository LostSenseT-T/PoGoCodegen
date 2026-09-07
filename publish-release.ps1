$ErrorActionPreference = 'Stop'

# Use a fresh directory for every release so stale publish files never enter the ZIP.
$releaseName = 'PoGoCodegen-Windows-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$releaseRoot = Join-Path $PSScriptRoot "artifacts/releases/$releaseName"
$publishFolder = Join-Path $releaseRoot 'PoGoCodegen'
$zipPath = "$releaseRoot.zip"
New-Item -ItemType Directory -Path $publishFolder | Out-Null

Push-Location $PSScriptRoot
try {
    dotnet publish PoGOQRCodesGenerator/PoGOQRCodesGenerator.csproj `
        -c Release -f net8.0-windows10.0.19041.0 `
        -p:BuildWindowsOnly=true -p:PublishProfile=WindowsZip `
        -p:Platform=x64 -p:UseRidGraph=true -o $publishFolder
    if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit code $LASTEXITCODE). ZIP was not created." }

    foreach ($requiredFile in @('PoGOQRCodesGenerator.exe', 'coreclr.dll', 'hostfxr.dll', 'Microsoft.UI.Xaml.dll', 'libSkiaSharp.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishFolder $requiredFile))) {
            throw "Publish output is incomplete: missing $requiredFile. ZIP was not created."
        }
    }

    @'
PoGoCodegen - Windows x64

1. Extract the entire ZIP to a folder.
2. Run PoGOQRCodesGenerator.exe inside the PoGoCodegen folder.

Keep all files and subfolders together. Do not run directly inside the ZIP.
.NET and Windows App SDK runtimes are included.
Requires Windows 10 version 1809 or later, or Windows 11 (x64).
Saved templates are stored separately in the current Windows user's app data.
'@ | Set-Content -LiteralPath (Join-Path $publishFolder 'START-HERE.txt') -Encoding UTF8

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($releaseRoot, $zipPath)
    Write-Host "Release ZIP: $zipPath"
}
finally {
    Pop-Location
}
