param([string]$OutputDirectory = 'src\Yukkuri.Desktop\Assets\qq-bridge')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
$vendor = Join-Path $projectRoot '.resources\minhook\minhook-1.3.4'
$archive = Join-Path $projectRoot '.resources\minhook-1.3.4.zip'
if (-not (Test-Path -LiteralPath $archive)) {
    New-Item -ItemType Directory -Path (Split-Path $archive) -Force | Out-Null
    Invoke-WebRequest -Uri 'https://codeload.github.com/TsudaKageyu/minhook/zip/refs/tags/v1.3.4' -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '172708123DAA0C98D20D3A980B16A50BE14AF243DC95DEE6F79C24193AD010E4') {
    throw 'MinHook source archive checksum mismatch.'
}
if (-not (Test-Path -LiteralPath (Join-Path $vendor 'include\MinHook.h'))) {
    Expand-Archive -LiteralPath $archive -DestinationPath (Split-Path $vendor) -Force
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'MSVC x64 Build Tools are required to build the QQ injector.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$build = Join-Path $projectRoot '.test-data\native-build'
New-Item -ItemType Directory -Path $build -Force | Out-Null
$compiler = Join-Path $vs 'Common7\Tools\VsDevCmd.bat'
$source = Join-Path $projectRoot 'src\Yukkuri.QQInjector\Injector.cpp'
$dll = Join-Path $output 'YukkuriQQInjector.dll'
$arguments = @(
    ('call "' + $compiler + '" -arch=x64 -host_arch=x64 >nul'),
    ('cl /nologo /c /O2 /MT /EHsc /W4 /WX /I "' + (Join-Path $vendor 'include') + '" "' + $source + '"'),
    ('cl /nologo /c /O2 /MT /W3 /wd4701 /I "' + (Join-Path $vendor 'include') +
        '" "' + (Join-Path $vendor 'src\buffer.c') + '" "' + (Join-Path $vendor 'src\hook.c') +
        '" "' + (Join-Path $vendor 'src\trampoline.c') + '" "' + (Join-Path $vendor 'src\hde\hde64.c') + '"'),
    ('link /nologo /DLL Injector.obj buffer.obj hook.obj trampoline.obj hde64.obj /IMPLIB:YukkuriQQInjector.lib /OUT:"' + $dll + '"')
)
# cmd is used only for the compiler environment, with fixed, quoted build paths.
Push-Location -LiteralPath $build
try {
    & $env:ComSpec /d /s /c ($arguments -join ' && ') 2>&1 | Write-Output
    if ($LASTEXITCODE -ne 0) { throw 'QQ injector compilation failed.' }
}
finally { Pop-Location }
Copy-Item -LiteralPath (Join-Path $vendor 'LICENSE.txt') -Destination (Join-Path $output 'MinHook-LICENSE.txt') -Force
