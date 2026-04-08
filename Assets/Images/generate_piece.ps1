Add-Type -AssemblyName System.Drawing

$width = 512
$height = 512
$bmp = New-Object System.Drawing.Bitmap($width, $height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

# Pens and Brushes
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::Black, 4)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)

function Get-EdgePoints ($sx, $sy, $ex, $ey, $sign) {
    $dx = $ex - $sx
    $dy = $ey - $sy
    $L = [Math]::Sqrt($dx * $dx + $dy * $dy)
    $ux = $dx / $L
    $uy = $dy / $L
    $nx = $uy
    $ny = -$ux
    
    $pts = @(
        @(0.15, 0.00), @(0.42, 0.00), @(0.42, 0.00),
        @(0.42, 0.15), @(0.20, 0.20), @(0.20, 0.35),
        @(0.20, 0.60), @(0.80, 0.60), @(0.80, 0.35),
        @(0.80, 0.20), @(0.58, 0.15), @(0.58, 0.00),
        @(0.58, 0.00), @(0.85, 0.00), @(1.00, 0.00)
    )
    
    $result = New-Object System.Drawing.PointF[] 15
    for ($i = 0; $i -lt 15; $i++) {
        $rx = $pts[$i][0]
        $ry = $pts[$i][1] * $sign
        $x = $sx + $rx * $L * $ux + $ry * $L * $nx
        $y = $sy + $rx * $L * $uy + $ry * $L * $ny
        $result[$i] = New-Object System.Drawing.PointF($x, $y)
    }
    return $result
}

$p0x = 156; $p0y = 156
$p1x = 356; $p1y = 156
$p2x = 356; $p2y = 356
$p3x = 156; $p3y = 356

$allPts = New-Object System.Drawing.PointF[] 61
$allPts[0] = New-Object System.Drawing.PointF($p0x, $p0y)

$idx = 1
$edges = @(
    Get-EdgePoints $p0x $p0y $p1x $p1y 1
    Get-EdgePoints $p1x $p1y $p2x $p2y 1
    Get-EdgePoints $p2x $p2y $p3x $p3y -1
    Get-EdgePoints $p3x $p3y $p0x $p0y -1
)

foreach ($edge in $edges) {
    foreach ($pt in $edge) {
        $allPts[$idx] = $pt
        $idx++
    }
}

$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddBeziers($allPts)
$path.CloseFigure()

$g.FillPath($brush, $path)
$g.DrawPath($pen, $path)

$outPath = "c:\Users\nakam\UnityProject\Jigsaw\Assets\Images\piece.png"
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)

$g.Dispose()
$bmp.Dispose()
$path.Dispose()

Write-Output "Generated piece.png successfully."
