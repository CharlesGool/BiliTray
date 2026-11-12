# 编译 BiliTray 便携版到 .\BiliTray\BiliTray.exe（只需要 Windows 自带的 .NET Framework 4.x）
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = $PSScriptRoot
$out  = Join-Path $root 'BiliTray'
New-Item -ItemType Directory -Force $out | Out-Null

# 生成 exe 图标（多尺寸 PNG 打进 .ico）
$ico = Join-Path $root 'src\BiliTray.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAlias'
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(251, 114, 153))), 0, 0, $s - 1, $s - 1)
    $font = New-Object System.Drawing.Font('Segoe UI Symbol', ($s * 0.55), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString([string][char]0x266B, $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(0, ($s / 32), $s, $s)), $sf)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}
$fs = [System.IO.File]::Create($ico); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $pngs[$i]
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$d.Length); $bw.Write([uint32]$offset)
    $offset += $d.Length
}
foreach ($d in $pngs) { $bw.Write($d) }
$bw.Close()

$fw  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
& "$fw\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 `
    "/out:$out\BiliTray.exe" "/win32icon:$ico" `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Web.Extensions.dll `
    "$root\src\BiliTray.cs"
if ($LASTEXITCODE -ne 0) { throw "编译失败" }
"OK -> $out\BiliTray.exe"
