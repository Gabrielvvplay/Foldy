# Desenha o ícone do programa (src\icone.ico). Só precisa rodar de novo se mudar o desenho.
# Uso: powershell -STA -ExecutionPolicy Bypass -File tools\gerar-icone.ps1 [-Estilo A|B|C|D] [-Previa arquivo.png]
#   -Previa  em vez de salvar o .ico, desenha os 4 estilos lado a lado nesse .png pra comparar.
param([string]$Estilo = 'C', [string]$Previa)
Add-Type -AssemblyName PresentationCore, WindowsBase, PresentationFramework
$ErrorActionPreference = 'Stop'

function Rgb([string]$hex) { [Windows.Media.ColorConverter]::ConvertFromString($hex) }
function Argb($a, $hex) { $c = Rgb $hex; [Windows.Media.Color]::FromArgb($a, $c.R, $c.G, $c.B) }
function Solid($color) { New-Object Windows.Media.SolidColorBrush($color) }
function Grad($top, $bottom) { New-Object Windows.Media.LinearGradientBrush((Rgb $top), (Rgb $bottom), 90) }
function Diag($a, $b) { New-Object Windows.Media.LinearGradientBrush((Rgb $a), (Rgb $b), (New-Object Windows.Point(0, 0)), (New-Object Windows.Point(1, 1))) }
function Box($x, $y, $w, $h) { New-Object Windows.Rect($x, $y, $w, $h) }

# Pasta de arquivo (aba em cima), no quadro 256x256.
$folderBack = [Windows.Media.Geometry]::Parse('M 20,72 Q 20,46 46,46 L 96,46 Q 108,46 116,56 L 126,70 L 210,70 Q 236,70 236,96 L 236,198 Q 236,224 210,224 L 46,224 Q 20,224 20,198 Z')

$apps = @(@('#5AB0FF', '#1E7BFF'), @('#4CE07A', '#1FB255'), @('#FFB340', '#FF8A00'), @('#FF6B95', '#E8245A'),
          @('#B48CFF', '#7F4DFF'), @('#FFE04A', '#F5B800'), @('#5EE3E0', '#14B8C4'), @('#FF7B6B', '#F0402F'), @('#9BE15D', '#4CB83A'))

function Tiles($dc, $colors, $x0, $y0, $cell, $gap, $cols, $radius) {
    for ($i = 0; $i -lt $colors.Count; $i++) {
        $x = $x0 + ($i % $cols) * ($cell + $gap); $y = $y0 + [math]::Floor($i / $cols) * ($cell + $gap)
        $dc.DrawRoundedRectangle((Grad $colors[$i][0] $colors[$i][1]), $null, (Box $x $y $cell $cell), $radius, $radius)
    }
}

function Draw($dc, $estilo) {
    switch ($estilo) {
        'A' { # Pasta amarela clássica, com os apps na frente
            $dc.DrawGeometry((Grad '#F7B23B' '#E08A0B'), $null, $folderBack)
            $dc.DrawRoundedRectangle((Grad '#FFD766' '#FFB21E'), $null, (Box 20 94 216 130), 24, 24)
            Tiles $dc @($apps[0], $apps[1], $apps[3], $apps[4]) 90 121 34 8 2 10
        }
        'B' { # Escura, igual às pastas do programa, com 9 apps
            $pen = New-Object Windows.Media.Pen((Solid (Argb 90 '#FFFFFF')), 4)
            $dc.DrawRoundedRectangle((Grad '#4A4A58' '#1E1E26'), $pen, (Box 10 10 236 236), 58, 58)
            Tiles $dc $apps 50 50 44 12 3 13
        }
        'C' { # Gradiente roxo/rosa com uma pasta branca
            $dc.DrawRoundedRectangle((Diag '#7B5CFF' '#FF4F9A'), $null, (Box 8 8 240 240), 58, 58)
            $dc.PushTransform((New-Object Windows.Media.TranslateTransform(48.6, 44)))
            $dc.PushTransform((New-Object Windows.Media.ScaleTransform(0.62, 0.62)))
            $dc.DrawGeometry((Solid (Argb 150 '#FFFFFF')), $null, $folderBack)
            $dc.DrawRoundedRectangle((Solid (Rgb '#FFFFFF')), $null, (Box 20 94 216 130), 24, 24)
            $dc.Pop(); $dc.Pop()
        }
        'D' { # Clara, com um app "saindo" da pasta (o arrastar pra fora)
            $pen = New-Object Windows.Media.Pen((Solid (Argb 60 '#000000')), 3)
            $dc.DrawRoundedRectangle((Grad '#FFFFFF' '#E3E9F2'), $pen, (Box 10 10 236 236), 58, 58)
            Tiles $dc @($apps[0], $apps[1], $apps[3]) 50 50 68 20 2 18
            $dc.DrawRoundedRectangle((Solid (Argb 40 '#000000')), $null, (Box 138 138 68 68), 18, 18)
            $dc.PushTransform((New-Object Windows.Media.RotateTransform(12, 186, 186)))
            $dc.DrawRoundedRectangle((Solid (Argb 55 '#000030')), $null, (Box 152 160 68 68), 18, 18)
            $dc.DrawRoundedRectangle((Grad $apps[2][0] $apps[2][1]), $null, (Box 152 152 68 68), 18, 18)
            $dc.Pop()
        }
    }
}

function Render([int]$size, [string]$estilo) {
    $dv = New-Object Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform(($size / 256.0), ($size / 256.0))))
    Draw $dc $estilo
    $dc.Pop(); $dc.Close()
    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $rtb
}

function Png($bmp) {
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream; $enc.Save($ms); , $ms.ToArray()
}

if ($Previa) {
    # Os 4 estilos lado a lado: grande, e pequeno (32px, como na barra de tarefas) sobre fundo claro e escuro.
    $dv = New-Object Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.DrawRectangle((Solid (Rgb '#F2F2F5')), $null, (Box 0 0 1240 420))
    $dc.DrawRectangle((Solid (Rgb '#202024')), $null, (Box 620 330 620 90))
    $font = New-Object Windows.Media.Typeface('Segoe UI Semibold')
    $i = 0
    foreach ($e in 'A', 'B', 'C', 'D') {
        $x = 30 + $i * 305
        $label = New-Object Windows.Media.FormattedText($e, [Globalization.CultureInfo]::InvariantCulture, 'LeftToRight', $font, 30, [Windows.Media.Brushes]::Black)
        $dc.DrawText($label, (New-Object Windows.Point(($x + 118), 8)))
        $dc.DrawImage((Render 256 $e), (Box $x 52 256 256))
        $small = Render 32 $e
        $dc.DrawImage($small, (Box ($x + 112) 359 32 32))
        $i++
    }
    $dc.Close()
    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap(1240, 420, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    [IO.File]::WriteAllBytes($Previa, (Png $rtb))
    return
}

$sizes = 256, 128, 64, 48, 32, 24, 16
$pngs = @($sizes | ForEach-Object { , (Png (Render $_ $Estilo)) })
$out = Join-Path $PSScriptRoot '..\src\icone.ico'
$w = New-Object IO.BinaryWriter([IO.File]::Create($out))
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$pngs[$i].Length); $w.Write([int]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write([byte[]]$p) }
$w.Close()
Write-Host "Ícone (estilo $Estilo) salvo em $out"
