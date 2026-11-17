# Generuje ikone aplikacji (tamborek z wyszytym sercem i igla) -> src\Wyszyciuszek\Assets\AppIcon.ico + AppIcon.png
# Uruchom w Windows PowerShell 5.1:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot '..\src\Wyszyciuszek\Assets'
New-Item -ItemType Directory -Force $outDir | Out-Null

function C([string]$hex, [int]$a = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    return [System.Drawing.Color]::FromArgb($a, $c.R, $c.G, $c.B)
}

function Brush([string]$hex, [int]$a = 255) { return New-Object System.Drawing.SolidBrush (C $hex $a) }

function Heart([float]$cx, [float]$cy, [float]$s) {
    # serce z dwoch lukow i czubka (s = polowa szerokosci)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.StartFigure()
    $p.AddBezier($cx, $cy - 0.35 * $s, $cx - 0.1 * $s, $cy - 1.0 * $s, $cx - 1.05 * $s, $cy - 1.0 * $s, $cx - 1.0 * $s, $cy - 0.25 * $s)
    $p.AddBezier($cx - 1.0 * $s, $cy - 0.25 * $s, $cx - 0.95 * $s, $cy + 0.35 * $s, $cx - 0.35 * $s, $cy + 0.65 * $s, $cx, $cy + 1.0 * $s)
    $p.AddBezier($cx, $cy + 1.0 * $s, $cx + 0.35 * $s, $cy + 0.65 * $s, $cx + 0.95 * $s, $cy + 0.35 * $s, $cx + 1.0 * $s, $cy - 0.25 * $s)
    $p.AddBezier($cx + 1.0 * $s, $cy - 0.25 * $s, $cx + 1.05 * $s, $cy - 1.0 * $s, $cx + 0.1 * $s, $cy - 1.0 * $s, $cx, $cy - 0.35 * $s)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    $g.ScaleTransform($s, $s)
    $detail = $size -ge 48

    $cx = 128; $cy = 138
    # --- zacisk tamborka (u gory) ---
    $g.FillRectangle((Brush '#8E6A3E'), 112, 8, 32, 30)
    $g.FillRectangle((Brush '#B8BCC2'), 104, 14, 48, 10)
    $g.FillEllipse((Brush '#D9DCE0'), 146, 10, 18, 18)

    # --- drewniana obrecz ---
    $wood = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 20), (New-Object System.Drawing.PointF 0, 256), (C '#E0B57A'), (C '#A97A43')
    $g.FillEllipse($wood, $cx - 116, $cy - 116, 232, 232)
    $g.FillEllipse((Brush '#7E5A31'), $cx - 102, $cy - 102, 204, 204)
    # --- plotno (len) ---
    $linen = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 40), (New-Object System.Drawing.PointF 0, 240), (C '#F4EEE2'), (C '#E2D7C1')
    $g.FillEllipse($linen, $cx - 96, $cy - 96, 192, 192)
    if ($detail) {
        # splot tkaniny
        $weave = New-Object System.Drawing.Pen (C '#C9BCA3' 70), 1
        $round = New-Object System.Drawing.Drawing2D.GraphicsPath
        $round.AddEllipse($cx - 96, $cy - 96, 192, 192)
        $g.SetClip($round)
        for ($i = 0; $i -lt 256; $i += 6) { $g.DrawLine($weave, $i, 0, $i, 256); $g.DrawLine($weave, 0, $i, 256, $i) }
        $g.ResetClip()
    }

    # --- wyszyte serce ---
    $heart = Heart $cx ($cy - 4) 62
    $g.FillPath((Brush '#E2574C'), $heart)
    if ($detail) {
        # sciegi satynowe (ukosne kreski) i obrys sciegiem
        $g.SetClip($heart)
        $satin = New-Object System.Drawing.Pen (C '#FF8A80' 150), 3
        for ($i = -200; $i -lt 300; $i += 9) { $g.DrawLine($satin, $i, 40, $i + 140, 240) }
        $g.ResetClip()
        $stitch = New-Object System.Drawing.Pen (C '#9E2F28'), 5
        $stitch.DashPattern = @(2.2, 1.4)
        $stitch.DashCap = 'Round'
        $g.DrawPath($stitch, $heart)
    } else {
        $g.DrawPath((New-Object System.Drawing.Pen (C '#9E2F28'), 6), $heart)
    }

    # --- igla z nitka ---
    if ($size -ge 32) {
        $thread = New-Object System.Drawing.Pen (C '#E2574C'), 4
        $thread.StartCap = 'Round'; $thread.EndCap = 'Round'
        $g.DrawBezier($thread, 186, 102, 222, 70, 236, 132, 214, 168)
        $needle = New-Object System.Drawing.Pen (C '#3A3F46'), 11
        $needle.StartCap = 'Round'; $needle.EndCap = 'Triangle'
        $g.DrawLine($needle, 226, 40, 168, 128)
        $shine = New-Object System.Drawing.Pen (C '#E8EBEF'), 6
        $shine.StartCap = 'Round'; $shine.EndCap = 'Triangle'
        $g.DrawLine($shine, 226, 40, 168, 128)
        $g.FillEllipse((Brush '#3A3F46'), 218, 38, 9, 9)
    }

    $g.Dispose()
    return $bmp
}

$sizes = @(256, 64, 48, 40, 32, 24, 20, 16)
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = Draw-Icon $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($sz -eq 256) { $bmp.Save((Join-Path $outDir 'AppIcon.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    $pngs += , @($sz, $ms.ToArray())
}

# --- zapis ICO (wpisy PNG) ---
$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ico
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $sz = $p[0]; $data = $p[1]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($p in $pngs) { $bw.Write([byte[]]$p[1]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $outDir 'AppIcon.ico'), $ico.ToArray())
Write-Host "Zapisano ikone do $outDir"
