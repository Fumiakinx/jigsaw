# 一発画像生成・自動1920x1080クロップ＆セーフゾーン最適化スクリプト
# utf-8（BOM付き）で保存

$ErrorActionPreference = "Stop"

# 1. 既存パズル画像の参考スタイル解析とパス設定
$picDir = "c:\Users\nakam\UnityProject\Jigsaw\_Picture"
$resourceImageDir = "c:\Users\nakam\UnityProject\Jigsaw\Assets\Resources\Images"
$outputImageDir = "c:\Users\nakam\UnityProject\Jigsaw\Assets\Images"

# _Picture内の既存画像をスキャン
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "   パズル画像スタイル解析・自動生成アシスト" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

$existingStyleDescription = "既存のパズル画像（_Picture内など）に見られる、高精細で色彩豊かなプレミアムジグソーパズルのデザインスタイル。適度な被写界深度と立体的なディテールがあり、目に優しい美しい照明が特徴。"

if (Test-Path $picDir) {
    $files = Get-ChildItem -Path $picDir -Include *.webp, *.png, *.jpg -Recurse
    if ($files.Count -gt 0) {
        Write-Host "[解析] _Picture 内に $($files.Count) 枚のパズル用画像を確認しました。" -ForegroundColor Green
        # ここで特定の画像名を参考としてプロンプトに盛り込む
        $sampleName = $files[0].Name
        $existingStyleDescription += " 具体的には、_Picture内の '$sampleName' のような調和の取れた色合い、高い解像度、そしてジグソーパズルのピースが映えやすいプレミアムな空気感と質感を踏襲してください。"
    }
}

# 2. ユーザーからの入力受付
Write-Host ""
Write-Host "生成したい画像のテーマや指示（プロンプト）を入力してください。" -ForegroundColor Yellow
Write-Host "例: レトロな喫茶店の窓辺、日差しが差し込む机の上に温かいコーヒー" -ForegroundColor DarkGray
$userPrompt = Read-Host "テーマ入力"

if ([string]::IsNullOrWhiteSpace($userPrompt)) {
    Write-Host "入力が空のため、デフォルトテーマ '静かな書斎とコーヒーブレイクの風景' で進めます。" -ForegroundColor Gray
    $userPrompt = "静かな書斎とコーヒーブレイクの風景"
}

# 3. セーフゾーンと比率（16:9）切り出しを意識した自動プロンプト合成
$safeZoneInstruction = "This image will be cropped to a 16:9 aspect ratio (1920x1080) for a puzzle. Keep all critical elements, subjects, focal points, and action strictly inside the central 60% vertical area (safe zone). The very top and very bottom will be cropped away, so only place background, sky, table surfaces, or flooring there. Ensure the composition looks rich and complete within this central 16:9 safe zone."
$styleInstruction = "High quality, vibrant colors, premium detail, suitable for a beautiful jigsaw puzzle game background. Avoid plain or neon blinding colors. Gentle to the eyes."

$finalPrompt = @"
[Theme]
$userPrompt

[Style & Reference]
$existingStyleDescription
$styleInstruction

[Crucial Composition (16:9 Safe Zone)]
$safeZoneInstruction
"@

# クリップボードに自動コピーして、貼り付けるだけで画像生成を実行できるようにする
try {
    Set-Clipboard -Value $finalPrompt
    $clipboardMsg = "（クリップボードに自動コピーしました！）"
} catch {
    $clipboardMsg = ""
}

Write-Host ""
Write-Host "--------------------------------------------------------" -ForegroundColor Cyan
Write-Host "【生成用最適化プロンプト】$clipboardMsg" -ForegroundColor Green
Write-Host "--------------------------------------------------------" -ForegroundColor Cyan
Write-Host $finalPrompt -ForegroundColor White
Write-Host "--------------------------------------------------------" -ForegroundColor Cyan
Write-Host ""

# 4. 生成画像のクロップ＆リサイズ処理フェーズ
Write-Host "画像生成が完了したら、生成された正方形画像ファイル（.png または .webp）のパスを入力するか、" -ForegroundColor Yellow
Write-Host "ファイルをこのウィンドウにドラッグ＆ドロップして Enter を押してください。" -ForegroundColor Yellow
$inputPath = Read-Host "画像ファイルのパス"

# クォーテーションなどのトリム
$inputPath = $inputPath.Trim().Trim('"').Trim("'")

if (-not (Test-Path $inputPath)) {
    Write-Error "指定されたファイルが見つかりません: $inputPath"
    Exit
}

# 保存先のファイル名決定
$defaultName = "jigsaw_new_1920x1080.png"
Write-Host ""
Write-Host "保存するファイル名を入力してください（デフォルト: $defaultName）" -ForegroundColor Yellow
$saveName = Read-Host "ファイル名"
if ([string]::IsNullOrWhiteSpace($saveName)) {
    $saveName = $defaultName
}
if (-not $saveName.EndsWith(".png")) {
    $saveName += ".png"
}

# 5. 高品質クロップ＆1920x1080リサイズ実行（GDI+使用）
Write-Host ""
Write-Host "[処理中] 画像の高品質センタークロップと 1920x1080 リサイズを実行中..." -ForegroundColor Cyan

Add-Type -AssemblyName System.Drawing

try {
    $srcImg = [System.Drawing.Image]::FromFile($inputPath)
    
    # ターゲット解像度 1920x1080 (16:9)
    $targetWidth = 1920
    $targetHeight = 1080
    
    # 元画像のサイズ
    $srcWidth = $srcImg.Width
    $srcHeight = $srcImg.Height
    
    # センタークロップ用の領域計算 (16:9比率での切り出し)
    # 正方形から16:9を切り出す場合、幅は元のままで、高さを「幅 * 9 / 16」にする
    $cropWidth = $srcWidth
    $cropHeight = [math]::Round($srcWidth * (9 / 16))
    
    if ($cropHeight -gt $srcHeight) {
        # 元画像がすでに横長すぎる場合
        $cropHeight = $srcHeight
        $cropWidth = [math]::Round($srcHeight * (16 / 9))
    }
    
    # 切り出しの開始座標（中心揃え）
    $cropX = [math]::Round(($srcWidth - $cropWidth) / 2)
    $cropY = [math]::Round(($srcHeight - $cropHeight) / 2)
    
    # 新規ビットマップ作成（1920x1080）
    $dstBmp = New-Object System.Drawing.Bitmap($targetWidth, $targetHeight)
    $g = [System.Drawing.Graphics]::FromImage($dstBmp)
    
    # 高画質化設定
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    
    # 描画元と描画先の矩形定義
    $srcRect = New-Object System.Drawing.Rectangle($cropX, $cropY, $cropWidth, $cropHeight)
    $dstRect = New-Object System.Drawing.Rectangle(0, 0, $targetWidth, $targetHeight)
    
    # クロップしながらリサイズ描画
    $g.DrawImage($srcImg, $dstRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    
    # 保存処理
    if (-not (Test-Path $resourceImageDir)) {
        New-Item -ItemType Directory -Force -Path $resourceImageDir | Out-Null
    }
    
    $finalPath = Join-Path $resourceImageDir $saveName
    $dstBmp.Save($finalPath, [System.Drawing.Imaging.ImageFormat]::Png)
    
    # 解放
    $g.Dispose()
    $dstBmp.Dispose()
    $srcImg.Dispose()
    
    Write-Host "[成功] 高解像度 1920x1080 画像の生成・切り出しに成功しました！" -ForegroundColor Green
    Write-Host "保存先: $finalPath" -ForegroundColor Green
    
    # Unityアセットフォルダ（Assets/Images）にも互換用にメインファイルを保存するか確認
    $compatPath = Join-Path $outputImageDir "CoffeeBreak.png"
    if (Test-Path $outputImageDir) {
        Copy-Item -Path $finalPath -Destination $compatPath -Force
        Write-Host "[成功] メイン背景互換用アセット（CoffeeBreak.png）も更新しました。" -ForegroundColor Green
    }
    
} catch {
    Write-Error "画像のクロップ・リサイズ処理中にエラーが発生しました: $_"
}

Write-Host ""
Write-Host "キーを押すと終了します..." -ForegroundColor Gray
[void][System.Console]::ReadKey($true)
