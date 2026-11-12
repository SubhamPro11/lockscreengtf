#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$OutputDir = "$PSScriptRoot/../artifacts/test-fixtures",
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath("$PSScriptRoot/..")
$ffmpeg = Join-Path $repo "LockScreenGif/Vendor/FFMPEG/ffmpeg.exe"

if (-not (Test-Path -LiteralPath $ffmpeg)) {
    throw "Bundled ffmpeg.exe not found at $ffmpeg"
}

if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
Write-Host "Generating test fixtures in: $OutputDir"

function Invoke-FFmpeg {
    param([string[]]$Arguments, [string]$OutputFile)
    if ((Test-Path -LiteralPath $OutputFile) -and -not $Force) {
        Write-Host "  [SKIP] Exists: $(Split-Path $OutputFile -Leaf)"
        return
    }
    Write-Host "  [GEN]  $(Split-Path $OutputFile -Leaf)"
    $psi = [System.Diagnostics.ProcessStartInfo]::new($ffmpeg)
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in $Arguments) { $psi.ArgumentList.Add($arg) }
    $proc = [System.Diagnostics.Process]::Start($psi)
    $err = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    if ($proc.ExitCode -ne 0) {
        throw "FFmpeg failed generating $(Split-Path $OutputFile -Leaf): $err"
    }
}

# 1. Standard geometries
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=1280x720:rate=30", "-t", "3", "-c:v", "libx264", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "clip-720p.mp4")) -OutputFile (Join-Path $OutputDir "clip-720p.mp4")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=1920x1080:rate=30", "-t", "3", "-c:v", "libx264", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "clip-1080p.mp4")) -OutputFile (Join-Path $OutputDir "clip-1080p.mp4")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=3840x2160:rate=30", "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "clip-4k.mp4")) -OutputFile (Join-Path $OutputDir "clip-4k.mp4")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=1080x1920:rate=30", "-t", "2", "-c:v", "libx264", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "clip-portrait.mp4")) -OutputFile (Join-Path $OutputDir "clip-portrait.mp4")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=2560x1080:rate=30", "-t", "2", "-c:v", "libx264", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "clip-ultrawide.mp4")) -OutputFile (Join-Path $OutputDir "clip-ultrawide.mp4")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=16x16:rate=10", "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "clip-tiny.mp4")) -OutputFile (Join-Path $OutputDir "clip-tiny.mp4")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=853x479:rate=24", "-t", "2", "-c:v", "ffv1", (Join-Path $OutputDir "clip-odd.mkv")) -OutputFile (Join-Path $OutputDir "clip-odd.mkv")

# 2. Formats & Codecs
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=640x360:rate=25", "-t", "2", "-c:v", "libvpx-vp9", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "clip-webm.webm")) -OutputFile (Join-Path $OutputDir "clip-webm.webm")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=640x360:rate=25", "-t", "2", "-c:v", "mpeg4", (Join-Path $OutputDir "clip-mkv.mkv")) -OutputFile (Join-Path $OutputDir "clip-mkv.mkv")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=640x360:rate=25", "-t", "2", "-c:v", "mjpeg", "-q:v", "3", (Join-Path $OutputDir "clip-avi.avi")) -OutputFile (Join-Path $OutputDir "clip-avi.avi")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=640x360:rate=25", "-t", "2", "-c:v", "libx264", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "clip-mov.mov")) -OutputFile (Join-Path $OutputDir "clip-mov.mov")

# 3. Timing variations
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=640x360:rate=60", "-t", "2", "-c:v", "libx264", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "clip-highfps.mp4")) -OutputFile (Join-Path $OutputDir "clip-highfps.mp4")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=640x360:rate=30", "-frames:v", "15", "-c:v", "libx264", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "clip-short.mp4")) -OutputFile (Join-Path $OutputDir "clip-short.mp4")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=640x360:rate=30", "-frames:v", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "clip-singleframe.mp4")) -OutputFile (Join-Path $OutputDir "clip-singleframe.mp4")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=640x360:rate=25", "-frames:v", "8", "-vf", "setpts=if(lt(N\,3)\,N*0.04\,0.12+(N-3)*0.12)/TB", "-c:v", "libx264", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "clip-vfr.mp4")) -OutputFile (Join-Path $OutputDir "clip-vfr.mp4")

# 4. GIF variations
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=15", "-t", "2", "-vf", "split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse", (Join-Path $OutputDir "clip-animated.gif")) -OutputFile (Join-Path $OutputDir "clip-animated.gif")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "color=c=black@0.0:size=320x240:rate=10,format=rgba", "-t", "1", (Join-Path $OutputDir "clip-transparent.gif")) -OutputFile (Join-Path $OutputDir "clip-transparent.gif")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=10", "-frames:v", "1", (Join-Path $OutputDir "clip-singleframe.gif")) -OutputFile (Join-Path $OutputDir "clip-singleframe.gif")

# 5. Path and Filename variations
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=10", "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "clip with spaces.mp4")) -OutputFile (Join-Path $OutputDir "clip with spaces.mp4")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=10", "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "clip_ล็อคสกรีน_テスト_🔒.mp4")) -OutputFile (Join-Path $OutputDir "clip_ล็อคสกรีน_テスト_🔒.mp4")
Invoke-FFmpeg -Arguments @("-y", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=10", "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", (Join-Path $OutputDir "-leading-dash-clip.mp4")) -OutputFile (Join-Path $OutputDir "-leading-dash-clip.mp4")

Write-Host "PASS: Test fixtures ready in $OutputDir"