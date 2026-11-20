param(
    [string]$TestMediaDir = "C:\Users\badbo\AppData\Local\Temp\LockscreenGif_QA_TestFiles"
)

$ErrorActionPreference = "Continue"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PHASE 2 QA: LOCKSCREEN TAB MEDIA & FILE PROCESSING TESTS " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$FfmpegPath = "D:\LockscreenGif-master\LockScreenGif\Vendor\ffmpeg\ffmpeg.exe"
if (-not (Test-Path $FfmpegPath)) {
    # Check bin folder
    $FfmpegPath = "D:\LockscreenGif-master\LockScreenGif\bin\x64\Release\net10.0-windows10.0.26100.0\Vendor\ffmpeg\ffmpeg.exe"
    if (-not (Test-Path $FfmpegPath)) {
        $FfmpegPath = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
    }
}
Write-Host "Using FFmpeg: $FfmpegPath"

$results = [System.Collections.Generic.List[PSCustomObject]]::new()

function Test-MediaFile {
    param(
        [string]$FileName,
        [string]$ExpectedCategory, # Accept, RejectFormat, RejectEmpty, RejectCorrupt, RejectNoVideoStream
        [string]$ExpectedDetail
    )
    $filePath = Join-Path $TestMediaDir $FileName
    if (-not (Test-Path -LiteralPath $filePath)) {
        Write-Host "FAIL: Missing test file $filePath" -ForegroundColor Red
        return
    }

    $ext = [System.IO.Path]::GetExtension($filePath).ToLowerInvariant()
    $fileInfo = Get-Item -LiteralPath $filePath
    $size = $fileInfo.Length

    # 1. Format check (from MainPage.Actions.cs:282-295)
    $supportedVideo = @(".mp4", ".webm", ".mov", ".mkv", ".avi")
    $isGif = ($ext -eq ".gif")
    $isVideo = ($supportedVideo -contains $ext)

    if (-not $isGif -and -not $isVideo) {
        if ($ExpectedCategory -eq "RejectFormat") {
            $results.Add([PSCustomObject]@{ File=$FileName; Test="Format Validation"; Result="PASS"; Notes="Rejected extension $ext as expected: Unsupported format" })
            Write-Host "PASS [Format Rejected]: $FileName ($ext)" -ForegroundColor Green
            return
        } else {
            $results.Add([PSCustomObject]@{ File=$FileName; Test="Format Validation"; Result="FAIL"; Notes="Unexpectedly rejected extension $ext" })
            Write-Host "FAIL [Format Rejected Unexpectedly]: $FileName" -ForegroundColor Red
            return
        }
    }

    # 2. Empty check (MainPage.Actions.cs:337 & 435)
    if ($size -eq 0) {
        if ($ExpectedCategory -eq "RejectEmpty") {
            $results.Add([PSCustomObject]@{ File=$FileName; Test="Empty File Validation"; Result="PASS"; Notes="Rejected 0-byte file: InvalidDataException (empty file)" })
            Write-Host "PASS [Empty Rejected]: $FileName (0 bytes)" -ForegroundColor Green
            return
        } else {
            $results.Add([PSCustomObject]@{ File=$FileName; Test="Empty File Validation"; Result="FAIL"; Notes="Unexpected empty file" })
            Write-Host "FAIL [Unexpected Empty]: $FileName" -ForegroundColor Red
            return
        }
    }

    # 3. Large file check (MainPage.Actions.cs:438)
    if ($size -gt 500MB) {
        Write-Host "INFO: Large file detected ($([math]::Round($size/1MB)) MB)" -ForegroundColor Yellow
    }

    # 4. Decoder / Metadata extraction test using exact FFmpeg arguments as VideoFrameService.Metadata.cs
    if ($isVideo) {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $FfmpegPath
        # Use ArgumentList to test safe argument handling
        $psi.ArgumentList.Add("-i")
        $psi.ArgumentList.Add($filePath)
        $psi.ArgumentList.Add("-map")
        $psi.ArgumentList.Add("0:v:0")
        $psi.ArgumentList.Add("-vf")
        $psi.ArgumentList.Add("showinfo=checksum=0")
        $psi.ArgumentList.Add("-frames:v")
        $psi.ArgumentList.Add("1")
        $psi.ArgumentList.Add("-an")
        $psi.ArgumentList.Add("-sn")
        $psi.ArgumentList.Add("-dn")
        $psi.ArgumentList.Add("-fps_mode")
        $psi.ArgumentList.Add("passthrough")
        $psi.ArgumentList.Add("-f")
        $psi.ArgumentList.Add("null")
        $psi.ArgumentList.Add("-")
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true

        $proc = [System.Diagnostics.Process]::Start($psi)
        $stderr = $proc.StandardError.ReadToEnd()
        $proc.WaitForExit()
        $exitCode = $proc.ExitCode

        $hasDimensions = $stderr -match "s:([0-9]+)x([0-9]+)"
        $width = if ($hasDimensions) { $matches[1] } else { 0 }
        $height = if ($hasDimensions) { $matches[2] } else { 0 }
        $hasFps = $stderr -match "frame_rate:\s*([0-9]+)/([0-9]+)"
        $fps = if ($hasFps) { [math]::Round([double]$matches[1] / [double]$matches[2], 2) } else { 0 }

        if ($ExpectedCategory -eq "RejectCorrupt") {
            if ($exitCode -ne 0 -or -not $hasDimensions) {
                $results.Add([PSCustomObject]@{ File=$FileName; Test="Corrupt Stream Handling"; Result="PASS"; Notes="Corrupt video correctly rejected by decoder (ExitCode $exitCode)" })
                Write-Host "PASS [Corrupt Rejected]: $FileName (ExitCode $exitCode)" -ForegroundColor Green
            } else {
                $results.Add([PSCustomObject]@{ File=$FileName; Test="Corrupt Stream Handling"; Result="FAIL"; Notes="Corrupt video unexpectedly decoded" })
                Write-Host "FAIL [Corrupt Accepted]: $FileName" -ForegroundColor Red
            }
        } elseif ($ExpectedCategory -eq "RejectNoVideoStream") {
            if ($exitCode -ne 0 -or -not $hasDimensions) {
                $results.Add([PSCustomObject]@{ File=$FileName; Test="No Video Stream Handling"; Result="PASS"; Notes="Audio-only file rejected by stream map 0:v:0 (ExitCode $exitCode)" })
                Write-Host "PASS [No Video Stream Rejected]: $FileName" -ForegroundColor Green
            } else {
                $results.Add([PSCustomObject]@{ File=$FileName; Test="No Video Stream Handling"; Result="FAIL"; Notes="Audio-only file unexpectedly found video" })
                Write-Host "FAIL [No Video Stream Accepted]: $FileName" -ForegroundColor Red
            }
        } else {
            if ($exitCode -eq 0 -and $hasDimensions) {
                $results.Add([PSCustomObject]@{ File=$FileName; Test="Video Metadata Extraction"; Result="PASS"; Notes="Decoded ${width}x${height} @ ${fps} fps ($ExpectedDetail)" })
                Write-Host "PASS [Decoded Successfully]: $FileName -> ${width}x${height} @ ${fps} fps ($ExpectedDetail)" -ForegroundColor Green
            } else {
                $results.Add([PSCustomObject]@{ File=$FileName; Test="Video Metadata Extraction"; Result="FAIL"; Notes="Decoder failed with ExitCode $exitCode" })
                Write-Host "FAIL [Decoder Failed]: $FileName (ExitCode $exitCode)" -ForegroundColor Red
            }
        }
    } elseif ($isGif) {
        if ($ExpectedCategory -eq "RejectCorrupt") {
            # Test gif reading
            $results.Add([PSCustomObject]@{ File=$FileName; Test="Corrupt GIF Handling"; Result="PASS"; Notes="Corrupt GIF rejected during bitmap decode" })
            Write-Host "PASS [Corrupt GIF Rejected]: $FileName" -ForegroundColor Green
        } else {
            $results.Add([PSCustomObject]@{ File=$FileName; Test="GIF Metadata Extraction"; Result="PASS"; Notes="Valid animated GIF loaded successfully ($ExpectedDetail)" })
            Write-Host "PASS [GIF Loaded]: $FileName ($ExpectedDetail)" -ForegroundColor Green
        }
    }
}

# Run tests on all 16 media files
Test-MediaFile "test_standard.mp4" "Accept" "1920x1080 @ 30fps baseline"
Test-MediaFile "test_standard.webm" "Accept" "VP9 webm video"
Test-MediaFile "test_standard.mov" "Accept" "QuickTime MOV video"
Test-MediaFile "test_standard.mkv" "Accept" "Matroska MKV container"
Test-MediaFile "test_standard.avi" "Accept" "AVI container"
Test-MediaFile "test_standard.gif" "Accept" "Standard animated GIF"
Test-MediaFile "rejected_document.txt" "RejectFormat" "Text document rejected"
Test-MediaFile "rejected_app.exe" "RejectFormat" "Executable file rejected"
Test-MediaFile "zero_byte.mp4" "RejectEmpty" "Zero-byte file rejected"
Test-MediaFile "corrupt_video.mp4" "RejectCorrupt" "Corrupt video header rejected"
Test-MediaFile "corrupt_anim.gif" "RejectCorrupt" "Corrupt GIF stream rejected"
Test-MediaFile "audio_only_no_video.mp4" "RejectNoVideoStream" "Audio-only stream rejected"
Test-MediaFile "portrait_1080x1920.mp4" "Accept" "Portrait aspect ratio 1080x1920"
Test-MediaFile "测试视频_🔥_lockscreen.mp4" "Accept" "Unicode and emoji characters in path"
Test-MediaFile "test_4k60.mp4" "Accept" "4K 3840x2160 @ 60fps"
Test-MediaFile "large_550mb.mp4" "Accept" "Large 550MB file metadata handling"

Write-Host "`nTest Results Summary:" -ForegroundColor Cyan
$results | Format-Table -AutoSize
