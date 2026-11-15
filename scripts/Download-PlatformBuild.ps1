param(
    [long]$RunId,
    [string]$Repository = 'Akilunafall/QQ-YukkuriVoice',
    [string]$OutputDirectory = 'dist\YukkuriVoice-1.3.0-CrossPlatform',
    [ValidatePattern('^$|^YukkuriVoice-(darwin|linux)-(x64|arm64)$')]
    [string]$ArtifactName = ''
)
$ErrorActionPreference = 'Stop'
if ($RunId -lt 1) { throw 'Supply a completed GitHub Actions run ID.' }
if ($Repository -ne 'Akilunafall/QQ-YukkuriVoice') { throw 'This helper is scoped to this project repository.' }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
if (-not $output.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The output directory must be inside this workspace.'
}
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'never'
$credentialResponse = "protocol=https`nhost=github.com`n`n" |
    git -c credential.interactive=never credential fill
if ($LASTEXITCODE -ne 0) { throw 'No existing GitHub Git credential is available.' }
$access = @{}
foreach ($line in $credentialResponse) {
    $parts = $line -split '=', 2
    if ($parts.Length -eq 2) { $access[$parts[0]] = $parts[1] }
}
if (-not $access.password) { throw 'GitHub authentication is unavailable.' }
$headers = @{
    Authorization = 'Bearer ' + $access.password
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent' = 'YukkuriVoice-PlatformBuild'
}
try {
    $api = 'https://api.github.com/repos/' + $Repository
    $run = Invoke-RestMethod -Uri ($api + '/actions/runs/' + $RunId) -Headers $headers
    if ($run.status -ne 'completed') { throw 'The requested build is still running.' }
    $artifacts = (Invoke-RestMethod -Uri ($api + '/actions/runs/' + $RunId + '/artifacts') -Headers $headers).artifacts
    if (-not $artifacts.Count) { throw 'The build has no downloadable artifacts.' }
    $cache = Join-Path $projectRoot ('.resources\platform-artifacts\' + $RunId)
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    foreach ($artifact in $artifacts) {
        if ($artifact.name -notmatch '^YukkuriVoice-(darwin|linux)-(x64|arm64)$') { continue }
        if ($ArtifactName -and $artifact.name -ne $ArtifactName) { continue }
        if ($artifact.expired) { throw 'A requested build artifact has expired.' }
        $archive = Join-Path $cache ($artifact.name + '.zip')
        if (-not (Test-Path -LiteralPath $archive)) {
            $partial = $archive + '.partial'
            Invoke-WebRequest -Uri ($api + '/actions/artifacts/' + $artifact.id + '/zip') -Headers $headers -OutFile $partial `
                -ConnectionTimeoutSeconds 30 -OperationTimeoutSeconds 900
            Move-Item -LiteralPath $partial -Destination $archive -Force
        }
        if ($artifact.digest -like 'sha256:*') {
            $actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
            if ($actual -ne $artifact.digest.Substring(7)) { throw 'Downloaded artifact integrity check failed.' }
        }
        Expand-Archive -LiteralPath $archive -DestinationPath $output -Force
        Write-Output ('Downloaded and verified ' + $artifact.name)
    }
}
finally {
    $headers.Clear()
    $access.Clear()
    $credentialResponse = $null
}
