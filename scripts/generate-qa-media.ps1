$ffmpeg = "D:\LockscreenGif-master\LockScreenGif\Vendor\FFMPEG\ffmpeg.exe"
$outDir = "C:\Users\badbo\AppData\Local\Temp\LockscreenGif_QA_TestFiles"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Write-Host "=== GENERATING SYNTHETIC TEST MEDIA ==="

# 1. Standard MP4 (1080p, 3s)
& $ffmpeg -y -f lavfi -i testsrc=duration=3:size=1920x1080:rate=30 -c:v libx264 -pix_fmt yuv420p "$outDir\test_standard.mp4" 2>$null

# 2. Standard WebM (1080p, 3s)
& $ffmpeg -y -f lavfi -i testsrc=duration=3:size=1920x1080:rate=30 -c:v libvpx-vp9 -pix_fmt yuv420p "$outDir\test_standard.webm" 2>$null

# 3. Standard MOV (1080p, 3s)
& $ffmpeg -y -f lavfi -i testsrc=duration=3:size=1920x1080:rate=30 -c:v mpeg4 "$outDir\test_standard.mov" 2>$null

# 4. Standard MKV (1080p, 3s)
& $ffmpeg -y -f lavfi -i testsrc=duration=3:size=1920x1080:rate=30 -c:v libx264 "$outDir\test_standard.mkv" 2>$null

# 5. Standard AVI (1080p, 3s)
& $ffmpeg -y -f lavfi -i testsrc=duration=3:size=1920x1080:rate=30 -c:v msmpeg4v2 "$outDir\test_standard.avi" 2>$null

# 6. Standard Animated GIF (480x270, 2s)
& $ffmpeg -y -f lavfi -i testsrc=duration=2:size=480x270:rate=15 "$outDir\test_standard.gif" 2>$null

# 7. Rejected file types
"This is a text file, not video." | Out-File "$outDir\rejected_document.txt" -Encoding utf8
"MZ dummy executable" | Out-File "$outDir\rejected_app.exe" -Encoding ascii

# 8. Zero-byte file
New-Item -ItemType File -Path "$outDir\zero_byte.mp4" -Force | Out-Null

# 9. Corrupt files (bad headers / garbage data)
[byte[]]$garbage = 1..1024 | ForEach-Object { Get-Random -Minimum 0 -Maximum 255 }
[System.IO.File]::WriteAllBytes("$outDir\corrupt_video.mp4", $garbage)
[System.IO.File]::WriteAllBytes("$outDir\corrupt_anim.gif", $garbage)

# 10. File with no video stream (Audio only AAC in MP4 container)
& $ffmpeg -y -f lavfi -i anullsrc=channel_layout=stereo:sample_rate=44100:duration=3 -c:a aac -vn "$outDir\audio_only_no_video.mp4" 2>$null

# 11. Portrait video (1080x1920, 3s)
& $ffmpeg -y -f lavfi -i testsrc=duration=3:size=1080x1920:rate=30 -c:v libx264 -pix_fmt yuv420p "$outDir\portrait_1080x1920.mp4" 2>$null

# 12. Path with Unicode characters & emojis
$unicodePath = "$outDir\测试视频_🔥_lockscreen.mp4"
Copy-Item "$outDir\test_standard.mp4" -Destination $unicodePath -Force

# 13. 4K60 video (3840x2160 @ 60fps, 2s)
& $ffmpeg -y -f lavfi -i testsrc=duration=2:size=3840x2160:rate=60 -c:v libx264 -preset ultrafast -pix_fmt yuv420p "$outDir\test_4k60.mp4" 2>$null

# 14. 550 MB large file (550MB sparse/random file with MP4 extension)
$largePath = "$outDir\large_550mb.mp4"
$fs = [System.IO.File]::Create($largePath)
$fs.SetLength(550MB)
$fs.Close()

Write-Host "=== TEST MEDIA GENERATED ==="
Get-ChildItem -Path $outDir | Select-Object Name, Length, LastWriteTime | Format-Table
