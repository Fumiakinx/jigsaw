Add-Type -AssemblyName System.Drawing

$paths = @(
    "c:\Users\nakam\UnityProject\Jigsaw\Assets\Images\PuzzleBase\donuts_1.png",
    "c:\Users\nakam\UnityProject\Jigsaw\Assets\Images\PuzzleBase\donuts_2.png",
    "c:\Users\nakam\UnityProject\Jigsaw\Assets\Images\PuzzleBase\donuts_3.png"
)

$targetWidth = 1920
$targetHeight = 1080

foreach ($path in $paths) {
    if (Test-Path $path) {
        Write-Host "Processing $path ..."
        $srcImg = [System.Drawing.Image]::FromFile($path)
        
        # Calculate crop area to maintain aspect ratio 16:9
        $targetRatio = $targetWidth / $targetHeight
        $srcRatio = $srcImg.Width / $srcImg.Height
        
        $srcRect = New-Object System.Drawing.Rectangle
        if ($srcRatio -gt $targetRatio) {
            # Source is wider than target
            $newWidth = [int]($targetRatio * $srcImg.Height)
            $x = [int](($srcImg.Width - $newWidth) / 2)
            $srcRect = New-Object System.Drawing.Rectangle($x, 0, $newWidth, $srcImg.Height)
        } else {
            # Source is taller than target
            $newHeight = [int]($srcImg.Width / $targetRatio)
            $y = [int](($srcImg.Height - $newHeight) / 2)
            $srcRect = New-Object System.Drawing.Rectangle(0, $y, $srcImg.Width, $newHeight)
        }

        $destBitmap = New-Object System.Drawing.Bitmap($targetWidth, $targetHeight)
        $g = [System.Drawing.Graphics]::FromImage($destBitmap)
        
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        
        $destRect = New-Object System.Drawing.Rectangle(0, 0, $targetWidth, $targetHeight)
        $g.DrawImage($srcImg, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
        
        $srcImg.Dispose()
        $g.Dispose()
        
        # Save to temp then overwrite to avoid lock
        $tempPath = $path + ".tmp.png"
        $destBitmap.Save($tempPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $destBitmap.Dispose()
        
        Move-Item -Path $tempPath -Destination $path -Force
        Write-Host "Successfully resized $path to 1920x1080"
    } else {
        Write-Warning "File not found: $path"
    }
}
