Add-Type -Path "D:\LockscreenGif-master\LockScreenGif\bin\x64\Release\net10.0-windows10.0.26100.0\LockscreenGif.dll"
$testFile = "C:\Users\badbo\AppData\Local\Temp\LockscreenGif_QA_TestFiles\测试视频_🔥_lockscreen.mp4"
$thumbTask = [LockscreenGif.Features.DesktopWallpaper.Services.Media.WallpaperMediaHelper]::GenerateThumbnailAsync($testFile)
$thumb = $thumbTask.GetAwaiter().GetResult()
Write-Host "Generated thumbnail: $thumb"
if ($thumb -and (Test-Path -LiteralPath $thumb)) {
    Write-Host "PASS: WallpaperMediaHelper safely generated thumbnail with ArgumentList!" -ForegroundColor Green
} else {
    Write-Host "FAIL: Thumbnail generation failed" -ForegroundColor Red
}
