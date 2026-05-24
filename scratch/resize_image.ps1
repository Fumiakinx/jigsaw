param (
    [string]$sourcePath,
    [string]$destinationPath
)

Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($sourcePath)

# 1024x1024から中央の1024x576（16:9）を切り出す
$cropWidth = 1024
$cropHeight = 576
$sourceX = 0
$sourceY = 224

$newImg = New-Object System.Drawing.Bitmap(1920, 1080)
$g = [System.Drawing.Graphics]::FromImage($newImg)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

$destRect = New-Object System.Drawing.Rectangle(0, 0, 1920, 1080)
$srcRect = New-Object System.Drawing.Rectangle($sourceX, $sourceY, $cropWidth, $cropHeight)
$units = [System.Drawing.GraphicsUnit]::Pixel

$g.DrawImage($img, $destRect, $srcRect, $units)

$newImg.Save($destinationPath, [System.Drawing.Imaging.ImageFormat]::Png)

$g.Dispose()
$img.Dispose()
$newImg.Dispose()
Write-Output "Done."
