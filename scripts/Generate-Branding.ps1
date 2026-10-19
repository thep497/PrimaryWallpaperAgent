[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
Add-Type -AssemblyName System.Drawing.Common
$brandingPath = Join-Path $script:ProjectRoot 'assets\branding'
if (-not (Test-Path -LiteralPath $brandingPath -PathType Container)) { throw 'Branding folder is missing.' }

function New-RoundedPath([single]$x,[single]$y,[single]$w,[single]$h,[single]$radius) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $d = 2 * $radius
    $path.AddArc($x,$y,$d,$d,180,90)
    $path.AddArc(($x+$w-$d),$y,$d,$d,270,90)
    $path.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90)
    $path.AddArc($x,($y+$h-$d),$d,$d,90,90)
    $path.CloseFigure()
    return $path
}
function Fill-Rounded($graphics,[string]$color,[single]$x,[single]$y,[single]$w,[single]$h,[single]$radius) {
    $path = New-RoundedPath $x $y $w $h $radius
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($color))
    try { $graphics.FillPath($brush,$path) } finally { $brush.Dispose(); $path.Dispose() }
}
function Stroke-Rounded($graphics,[string]$color,[single]$width,[single]$x,[single]$y,[single]$w,[single]$h,[single]$radius) {
    $path = New-RoundedPath $x $y $w $h $radius
    $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($color),$width)
    try { $graphics.DrawPath($pen,$path) } finally { $pen.Dispose(); $path.Dispose() }
}
function Fill-Polygon($graphics,[string]$color,[single[]]$coordinates) {
    $points = [Drawing.PointF[]]::new($coordinates.Length / 2)
    for ($i=0; $i -lt $points.Length; $i++) { $points[$i]=[Drawing.PointF]::new($coordinates[2*$i],$coordinates[2*$i+1]) }
    $brush=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($color))
    try { $graphics.FillPolygon($brush,$points) } finally { $brush.Dispose() }
}
function Draw-Small($graphics,[string]$state) {
    # Coordinates are on a 16px grid. Dual light/dark rims retain contrast on both taskbar themes.
    Fill-Rounded $graphics '#EDF7FF' 0.25 4.25 10.5 8.5 1.5
    Fill-Rounded $graphics '#172C46' 0.75 4.75 9.5 7.5 1
    Fill-Rounded $graphics '#070D16' 1.5 5.5 8 5.25 0.4
    Fill-Rounded $graphics '#EDF7FF' 3.5 1 12.25 10.5 1.7
    Fill-Rounded $graphics '#172C46' 4 1.5 11.25 9.5 1.2
    Fill-Rounded $graphics '#39D2E3' 5 2.5 9.25 6.5 0.45
    Fill-Polygon $graphics '#147CA9' @(5,9,8.5,5.5,11,7.5,14.25,4.5,14.25,9)
    Fill-Rounded $graphics '#EDF7FF' 8.1 11.4 2.8 2.3 0.4
    Fill-Rounded $graphics '#172C46' 8.75 11.5 1.5 2 0.3
    Fill-Rounded $graphics '#EDF7FF' 5.75 13 7.5 1.8 0.7
    Fill-Rounded $graphics '#172C46' 6.25 13.5 6.5 0.8 0.3
    if ($state -ne 'Running') {
        Fill-Rounded $graphics '#172C46' 9.25 9 6.75 7 1.3
        Fill-Rounded $graphics '#F7C65A' 9.9 9.65 5.45 5.7 0.8
        if ($state -eq 'Paused') {
            Fill-Rounded $graphics '#172C46' 11 10.8 1.1 3.3 0.2
            Fill-Rounded $graphics '#172C46' 13.1 10.8 1.1 3.3 0.2
        } else {
            Fill-Rounded $graphics '#172C46' 12 10.4 1.3 2.8 0.25
            Fill-Rounded $graphics '#172C46' 12 13.8 1.3 0.9 0.3
        }
    }
}
function Draw-Large($graphics) {
    foreach ($x in @(12,176)) {
        Fill-Rounded $graphics '#EDF7FF' $x 81 68 98 12
        Fill-Rounded $graphics '#172C46' ($x+3) 84 62 92 9
        Fill-Rounded $graphics '#070D16' ($x+8) 91 52 68 5
        Fill-Rounded $graphics '#EDF7FF' ($x+28) 177 12 14 3
        Fill-Rounded $graphics '#172C46' ($x+31) 177 6 12 2
        Fill-Rounded $graphics '#EDF7FF' ($x+13) 188 42 9 4
        Fill-Rounded $graphics '#172C46' ($x+16) 191 36 3 1
    }
    Fill-Rounded $graphics '#EDF7FF' 45 36 166 152 18
    Fill-Rounded $graphics '#172C46' 49 40 158 144 14
    $clip=New-RoundedPath 57 48 142 119 9
    $saved=$graphics.Save()
    $gradient=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.PointF]::new(57,48),[Drawing.PointF]::new(180,167),[Drawing.ColorTranslator]::FromHtml('#1769EC'),[Drawing.ColorTranslator]::FromHtml('#39D2E3'))
    $sun=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#F7F5CE'))
    try {
        $graphics.SetClip($clip)
        $graphics.FillRectangle($gradient,[single]57,[single]48,[single]142,[single]119)
        $graphics.FillEllipse($sun,[single]155,[single]65,[single]22,[single]22)
        Fill-Polygon $graphics '#174C7C' @(57,139,98,94,124,119,151,87,199,141,199,167,57,167)
        Fill-Polygon $graphics '#18BDA6' @(57,151,99,124,134,150,174,111,199,135,199,167,57,167)
    } finally { $graphics.Restore($saved); $gradient.Dispose(); $sun.Dispose(); $clip.Dispose() }
    Fill-Rounded $graphics '#39D2E3' 112 174 32 3 1.5
    Fill-Rounded $graphics '#EDF7FF' 108 187 40 29 5
    Fill-Rounded $graphics '#172C46' 112 187 32 25 3
    Fill-Rounded $graphics '#EDF7FF' 76 210 104 14 7
    Fill-Rounded $graphics '#172C46' 80 214 96 6 3
}
function New-IconBitmap([int]$size,[string]$state) {
    $bitmap=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        if ($state -eq 'Application' -and $size -ge 48) {
            $graphics.ScaleTransform($size/256.0,$size/256.0)
            Draw-Large $graphics
        } else {
            $graphics.ScaleTransform($size/16.0,$size/16.0)
            Draw-Small $graphics $(if($state -eq 'Application'){'Running'}else{$state})
        }
    } finally { $graphics.Dispose() }
    return $bitmap
}
function Write-Icon([string]$name,[string]$state,[int[]]$sizes) {
    $frames=[Collections.Generic.List[byte[]]]::new()
    foreach ($size in $sizes) {
        $bitmap=New-IconBitmap $size $state
        $stream=[IO.MemoryStream]::new()
        try { $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png); $frames.Add($stream.ToArray()) }
        finally { $stream.Dispose(); $bitmap.Dispose() }
    }
    $file=[IO.File]::Create((Join-Path $brandingPath $name))
    $writer=[IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Length)
        $offset=6+16*$sizes.Length
        for($i=0;$i -lt $sizes.Length;$i++) {
            $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
            $offset+=$frames[$i].Length
        }
        foreach($frame in $frames){$writer.Write([byte[]]$frame)}
    } finally { $writer.Dispose(); $file.Dispose() }
}
Write-Icon 'Application.ico' 'Application' @(16,20,24,32,40,48,64,128,256)
foreach($state in @('Running','Paused','Attention')) {
    Write-Icon ('Tray'+$state+'.ico') $state @(16,20,24,32,40,48,64)
}

# Editable SVG review master for the large mark; the script is the reproducible source.
$svg=@'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" role="img" aria-label="Primary Wallpaper Agent">
<defs><linearGradient id="sky" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#1769EC"/><stop offset="1" stop-color="#39D2E3"/></linearGradient><clipPath id="screen"><rect x="57" y="48" width="142" height="119" rx="9"/></clipPath></defs>
<g id="secondary-left"><rect x="12" y="81" width="68" height="98" rx="12" fill="#EDF7FF"/><rect x="15" y="84" width="62" height="92" rx="9" fill="#172C46"/><rect x="20" y="91" width="52" height="68" rx="5" fill="#070D16"/><rect x="40" y="177" width="12" height="14" rx="3" fill="#EDF7FF"/><rect x="43" y="177" width="6" height="12" rx="2" fill="#172C46"/><rect x="25" y="188" width="42" height="9" rx="4" fill="#EDF7FF"/><rect x="28" y="191" width="36" height="3" rx="1" fill="#172C46"/></g>
<use href="#secondary-left" transform="translate(164 0)"/>
<rect x="45" y="36" width="166" height="152" rx="18" fill="#EDF7FF"/><rect x="49" y="40" width="158" height="144" rx="14" fill="#172C46"/>
<g clip-path="url(#screen)"><path fill="url(#sky)" d="M57 48h142v119H57z"/><circle cx="166" cy="76" r="11" fill="#F7F5CE"/><path fill="#174C7C" d="M57 139 98 94 124 119 151 87 199 141V167H57z"/><path fill="#18BDA6" d="M57 151 99 124 134 150 174 111 199 135V167H57z"/></g>
<rect x="112" y="174" width="32" height="3" rx="1.5" fill="#39D2E3"/><rect x="108" y="187" width="40" height="29" rx="5" fill="#EDF7FF"/><rect x="112" y="187" width="32" height="25" rx="3" fill="#172C46"/><rect x="76" y="210" width="104" height="14" rx="7" fill="#EDF7FF"/><rect x="80" y="214" width="96" height="6" rx="3" fill="#172C46"/>
</svg>
'@
[IO.File]::WriteAllText((Join-Path $brandingPath 'Application.svg'),$svg,[Text.UTF8Encoding]::new($false))

$preview=[Drawing.Bitmap]::new(1100,640)
$g=[Drawing.Graphics]::FromImage($preview)
$font=[Drawing.Font]::new('Segoe UI',12)
$titleFont=[Drawing.Font]::new('Segoe UI',19,[Drawing.FontStyle]::Bold)
$labelFont=[Drawing.Font]::new('Segoe UI',10)
try {
    $g.Clear([Drawing.ColorTranslator]::FromHtml('#E8EDF4'))
    $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    foreach($panel in @(@{X=0;Background='#F5F7FB';Text='#172C46';Caption='LIGHT'},@{X=550;Background='#101B2D';Text='#EAF4FF';Caption='DARK'})) {
        $bg=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($panel.Background))
        $ink=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($panel.Text))
        try {
            $g.FillRectangle($bg,[int]$panel.X,0,550,640)
            $g.DrawString('Primary Wallpaper Agent',$titleFont,$ink,[single]($panel.X+28),[single]22)
            $g.DrawString($panel.Caption+' BACKGROUND',$labelFont,$ink,[single]($panel.X+28),[single]59)
            $mark=New-IconBitmap 256 'Application'
            try {$g.DrawImage($mark,[int]($panel.X+147),88,256,256)} finally {$mark.Dispose()}
            $g.DrawString('TRAY · actual pixel sizes',$font,$ink,[single]($panel.X+28),[single]350)
            $row=0
            foreach($state in @('Running','Paused','Attention')) {
                $y=400+$row*65
                $g.DrawString($state,$font,$ink,[single]($panel.X+28),[single]$y)
                $column=0
                foreach($size in @(16,20,24,32,40)) {
                    $x=$panel.X+175+$column*65
                    $iconBitmap=New-IconBitmap $size $state
                    try {$g.DrawImageUnscaled($iconBitmap,$x,$y)} finally {$iconBitmap.Dispose()}
                    $g.DrawString([string]$size,$labelFont,$ink,[single]$x,[single]($y+42))
                    $column++
                }
                $row++
            }
        } finally {$bg.Dispose();$ink.Dispose()}
    }
    $preview.Save((Join-Path $brandingPath 'Preview.png'),[Drawing.Imaging.ImageFormat]::Png)
} finally {$g.Dispose();$font.Dispose();$titleFont.Dispose();$labelFont.Dispose();$preview.Dispose()}
Write-Host 'Generated app/tray ICO files, SVG master and light/dark preview in assets/branding.'
