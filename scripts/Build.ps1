param([switch]$SkipTests, [switch]$RequireSdks, [switch]$PrepareBridge,
    [string]$OutputDirectory = 'dist\YukkuriVoice-1.2.1')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
& (Join-Path $PSScriptRoot 'New-Icon.ps1')
& (Join-Path $PSScriptRoot 'Build-QQInjector.ps1')
if (-not $SkipTests) {
    $node = (Get-Command node -ErrorAction SilentlyContinue).Source
    if (-not $node) { $node = Join-Path $env:ProgramFiles 'nodejs\node.exe' }
    if (-not (Test-Path -LiteralPath $node)) { throw 'Node.js is required for the local QQ transport tests.' }
    & $node (Join-Path $PSScriptRoot 'Test-LocalQQ.cjs')
    if ($LASTEXITCODE -ne 0) { throw 'Local QQ transport checks failed.' }
}
$bridgeRoot = Join-Path $releaseRoot 'bridge-x86'
dotnet publish (Join-Path $projectRoot 'src\Yukkuri.AquesBridge') -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $bridgeRoot
if ($LASTEXITCODE -ne 0) { throw 'x86 bridge publish failed.' }
if (-not $SkipTests) {
    $smokeBridge = Join-Path $projectRoot 'tests\Yukkuri.Smoke\bin\Release\net10.0-windows\bridge-x86'
    [System.IO.Directory]::CreateDirectory($smokeBridge) | Out-Null
    Copy-Item -LiteralPath (Join-Path $bridgeRoot 'YukkuriAquesBridge.exe') -Destination $smokeBridge -Force
    $smokeArguments = @((Join-Path $projectRoot '.test-data'))
    if ($RequireSdks) { $smokeArguments += '--require-sdks' }
    if ($PrepareBridge) { $smokeArguments += '--prepare-bridge' }
    dotnet run --project (Join-Path $projectRoot 'tests\Yukkuri.Smoke') -c Release -- @smokeArguments
    if ($LASTEXITCODE -ne 0) { throw 'Smoke checks failed.' }
}
dotnet publish (Join-Path $projectRoot 'src\Yukkuri.Desktop') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $releaseRoot
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\使用说明.html') -Destination $releaseRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $releaseRoot -Force
$licenseRoot = Join-Path $releaseRoot 'licenses'
[System.IO.Directory]::CreateDirectory($licenseRoot) | Out-Null
$dotnetRoot = Split-Path (Get-Command dotnet).Source
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'LICENSE.txt') -Destination (Join-Path $licenseRoot 'Microsoft-dotnet-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') -Destination (Join-Path $licenseRoot 'dotnet-ThirdPartyNotices.txt') -Force
Compress-Archive -LiteralPath $releaseRoot -DestinationPath ($releaseRoot + '-Windows-x64.zip') -Force
Write-Output "Portable app: $releaseRoot\YukkuriVoice.exe"
