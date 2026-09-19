param([switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$portableSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $portableSdk) {
    $dotnet = $portableSdk
    $env:DOTNET_ROOT = Split-Path -Parent $portableSdk
} else {
    $sdkCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $sdkCommand) { throw 'A .NET 10 SDK is required. This script does not download or install it.' }
    $dotnet = $sdkCommand.Source
}
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.packages\http-cache'
$restoreArguments = @()
if ($NoRestore) { $restoreArguments += '--no-restore' }
& $dotnet build (Join-Path $projectRoot 'src\Jellyfin.Plugin.SyncPlayNames.csproj') -c Release @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
& $dotnet run --project (Join-Path $projectRoot 'tests\SyncPlayNames.Tests.csproj') -c Release @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Plugin tests failed.' }
$artifacts = Join-Path $projectRoot 'artifacts'
$packageFolder = Join-Path $artifacts 'SyncPlayNames_0.1.0.0'
New-Item -ItemType Directory -Path $packageFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\bin\Release\net10.0\Jellyfin.Plugin.SyncPlayNames.dll') -Destination $packageFolder -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'meta.json') -Destination $packageFolder -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageFolder -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $packageFolder -Force
$archive = Join-Path $artifacts 'SyncPlayNames_0.1.0.0.zip'
Compress-Archive -LiteralPath $packageFolder -DestinationPath $archive -Force
Write-Output "Installable package: $archive"
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
