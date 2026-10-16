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
& $dotnet build (Join-Path $projectRoot 'src\Jellyfin.Plugin.SyncPlayNames.csproj') -c Release -p:JellyfinVersion=12.1.0 @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
& $dotnet run --project (Join-Path $projectRoot 'tests\SyncPlayNames.Tests.csproj') -c Release -p:JellyfinVersion=12.1.0 @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Plugin tests failed.' }
$artifacts = Join-Path $projectRoot 'artifacts'
$metadata = Get-Content -LiteralPath (Join-Path $projectRoot 'meta.json') -Raw | ConvertFrom-Json
$packageName = 'SyncPlayNames_' + $metadata.version
$packageFolder = Join-Path $artifacts $packageName
New-Item -ItemType Directory -Path $packageFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\bin\Release\net10.0\Jellyfin.Plugin.SyncPlayNames.dll') -Destination $packageFolder -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'meta.json') -Destination $packageFolder -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageFolder -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $packageFolder -Force
$dll = Join-Path $packageFolder 'Jellyfin.Plugin.SyncPlayNames.dll'
if ([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString() -ne $metadata.version) { throw 'Assembly and package versions do not match.' }
$dllBytes = [IO.File]::ReadAllBytes($dll)
foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
    if ($encoding.GetString($dllBytes) -match '(?i)[A-Z]:\\Users\\|/home/[^/\x00]+/|/Users/[^/\x00]+/') {
        throw 'Build-machine path found in release DLL.'
    }
}
$archive = Join-Path $artifacts ($packageName + '.zip')
# Jellyfin's catalog installer expects the DLL at the root of the ZIP.
$packageFiles = @('Jellyfin.Plugin.SyncPlayNames.dll', 'meta.json', 'README.md', 'LICENSE') | ForEach-Object { Join-Path $packageFolder $_ }
Compress-Archive -LiteralPath $packageFiles -DestinationPath $archive -Force
$sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $artifacts 'SHA256SUMS.txt'), "$sha256  $packageName.zip`n", [Text.UTF8Encoding]::new($false))
$versionParts = $metadata.version.Split('.')
$tag = 'v' + ($versionParts[0..2] -join '.')
$entry = [ordered]@{
    guid = $metadata.guid
    name = $metadata.name
    description = $metadata.description
    overview = $metadata.overview
    owner = $metadata.owner
    category = $metadata.category
    versions = @([ordered]@{
        version = $metadata.version
        changelog = $metadata.changelog
        targetAbi = $metadata.targetAbi
        sourceUrl = "https://github.com/isaun88/jellyfin-plugin-syncplay-names/releases/download/$tag/$packageName.zip"
        # MD5 is the checksum format required by Jellyfin's plugin catalog.
        checksum = (Get-FileHash -LiteralPath $archive -Algorithm MD5).Hash.ToLowerInvariant()
        timestamp = [DateTime]::UtcNow.ToString('o')
    })
}
[IO.File]::WriteAllText((Join-Path $projectRoot 'manifest.json'), (ConvertTo-Json -InputObject @($entry) -Depth 8) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Output "Installable package: $archive"
Write-Output "SHA256: $sha256"
Write-Output 'Updated manifest.json for this exact archive. Publish it after the matching release asset is available.'
