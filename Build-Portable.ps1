param([Parameter(Mandatory=$true)][string]$OcExe)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$cli = (Resolve-Path -LiteralPath $OcExe).Path
$portable = Join-Path $root 'portable-v36'
dotnet publish (Join-Path $root 'src/WinOCP.csproj') -c Release -r win-x64 --self-contained true -o $portable
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
New-Item -ItemType Directory -Force (Join-Path $portable 'tools') | Out-Null
Copy-Item -LiteralPath $cli -Destination (Join-Path $portable 'tools/oc.exe')
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $portable
$packageItems = @(Get-ChildItem -LiteralPath $portable | Where-Object { $_.Name -notmatch '^(connections\.|automation[.-]|settings\.json$)' } | Select-Object -ExpandProperty FullName)
Compress-Archive -Path $packageItems -DestinationPath (Join-Path $root 'WinOCP-win-x64.zip') -Force
