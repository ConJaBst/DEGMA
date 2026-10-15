# Builds the single-file DEGMA.exe using the same settings as Visual Studio's
# "FolderProfile" publish profile. Output: DEGMA\bin\Release\net10.0\publish\
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'DEGMA\DEGMA.csproj'
$out = Join-Path $PSScriptRoot 'DEGMA\bin\Release\net10.0\publish'

dotnet publish $project -c Release -p:PublishProfile=FolderProfile "-p:PublishDir=$out\\"
if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit code $LASTEXITCODE)" }

$exe = Join-Path $out 'DEGMA.exe'
Write-Host "`nPublished: $exe ($([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB)"
