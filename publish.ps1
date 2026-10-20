# Builds the single-file DEGMA.exe using the same settings as Visual Studio's
# "FolderProfile" publish profile, then packs the release zip.
#
#   .\publish.ps1                  -> DEGMA\bin\Release\net10.0\DEGMA-win-x64.zip
#   .\publish.ps1 -Version v3.3    -> DEGMA\bin\Release\net10.0\DEGMA-v3.3-win-x64.zip
#
# The zip is both a new install and an update: unzip it into the install folder.
# It holds DEGMA.exe, appsettings.Defaults.json and wwwroot only, never degma.db or the
# clinic's own appsettings.json, so updating can't overwrite their data or settings.
param([string]$Version = '')

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'DEGMA\DEGMA.csproj'
$out = Join-Path $PSScriptRoot 'DEGMA\bin\Release\net10.0\publish'
$pubxml = Join-Path $PSScriptRoot 'DEGMA\Properties\PublishProfiles\FolderProfile.pubxml'

# Without the profile, dotnet publish silently falls back to the csproj settings
if (-not (Test-Path $pubxml)) { throw "Publish profile not found: $pubxml" }

# Start clean so files from older builds can't end up in the zip
if (Test-Path $out) { Remove-Item -Recurse -Force $out }

dotnet publish $project -c Release -p:PublishProfile=FolderProfile "-p:PublishDir=$out\\"
if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit code $LASTEXITCODE)" }

$exe = Join-Path $out 'DEGMA.exe'
Write-Host "`nPublished: $exe ($([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB)"

$name = if ($Version) { "DEGMA-$Version-win-x64.zip" } else { 'DEGMA-win-x64.zip' }
$zip = Join-Path (Split-Path $out) $name
if (Test-Path $zip) { Remove-Item $zip }
$contents = 'DEGMA.exe', 'appsettings.Defaults.json', 'wwwroot' | ForEach-Object { Join-Path $out $_ }
foreach ($item in $contents) { if (-not (Test-Path $item)) { throw "Missing from publish output: $item" } }
Compress-Archive -Path $contents -DestinationPath $zip
Write-Host "Release zip: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
