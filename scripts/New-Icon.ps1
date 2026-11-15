$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Join-Path $PSScriptRoot '..\src\Yukkuri.Desktop\Assets'
[System.IO.Directory]::CreateDirectory($assetRoot) | Out-Null
$bitmap = [System.Drawing.Bitmap]::new(256, 256)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(183, 53, 72))
$white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
$pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 15)
$graphics.DrawLine($pen, 56, 120, 56, 142)
$graphics.DrawLine($pen, 91, 87, 91, 175)
$graphics.DrawLine($pen, 128, 57, 128, 199)
$graphics.DrawLine($pen, 165, 94, 165, 168)
$graphics.DrawLine($pen, 201, 117, 201, 145)
$bitmap.Save((Join-Path $assetRoot 'app.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream = [System.IO.File]::Create((Join-Path $assetRoot 'app.ico'))
$icon.Save($stream)
$stream.Dispose()
$icon.Dispose()
$pen.Dispose()
$white.Dispose()
$graphics.Dispose()
$bitmap.Dispose()
