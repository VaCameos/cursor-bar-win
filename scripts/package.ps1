$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$version = "0.1.0"
$props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
if ($props -match "<Version>([^<]+)</Version>") {
    $version = $Matches[1]
}

dotnet run --project tests/CursorBar.Core.Check/CursorBar.Core.Check.csproj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

function Publish-Rid([string]$rid) {
    $out = Join-Path $root "dist\$rid"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    New-Item -ItemType Directory -Force -Path $out | Out-Null

    dotnet publish src/CursorBar/CursorBar.csproj `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $out
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Copy-Item (Join-Path $root "scripts\使用说明.txt") (Join-Path $out "使用说明.txt")
    Copy-Item (Join-Path $root "scripts\install.ps1") (Join-Path $out "install.ps1")

    $zipName = "CursorBar-$version-$rid.zip"
    $zip = Join-Path $root "dist\$zipName"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
    Write-Host "Packed $zip"
}

Publish-Rid "win-x64"
Publish-Rid "win-arm64"

Set-Content -Path (Join-Path $root "dist\version.txt") -Value $version -NoNewline

$releases = Join-Path $root "releases"
New-Item -ItemType Directory -Force -Path $releases | Out-Null
Copy-Item (Join-Path $root "dist\CursorBar-$version-win-x64.zip") $releases -Force
Copy-Item (Join-Path $root "dist\CursorBar-$version-win-arm64.zip") $releases -Force
Write-Host "Copied zips to releases\"
