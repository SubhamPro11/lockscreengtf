using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LockscreenGif.Services;

/// <summary>
/// Translates internal runtime exceptions and Win32/COM error codes into
/// plain-language, actionable guidance for users without exposing raw stack traces.
/// </summary>
public static class HumanErrorTranslator
{
    public static string Translate(Exception? ex, string? fallback = null)
    {
        if (ex is null)
        {
            return fallback ?? "An unknown problem occurred.";
        }

        // Check for specific inner exception if wrapped
        var root = ex.GetBaseException();

        if (root is UnauthorizedAccessException)
        {
            return "Windows denied access to the lock-screen cache or file. "
                + "Ensure you grant permission if prompted, or verify your Windows account has administrator rights.";
        }

        if (root is FileNotFoundException fnf)
        {
            var fileName = !string.IsNullOrEmpty(fnf.FileName) ? Path.GetFileName(fnf.FileName) : "A required file";
            return $"{fileName} could not be found. Check that the file was not moved, renamed, or deleted.";
        }

        if (root is DirectoryNotFoundException)
        {
            return "A required folder could not be found. Check that your user profile and temporary folders are accessible.";
        }

        if (root is PathTooLongException)
        {
            return "The file path is too long for Windows to process. Move the file to a folder with a shorter path and retry.";
        }

        if (root is Win32Exception w32)
        {
            return w32.NativeErrorCode switch
            {
                1223 => "The administrator permission prompt was cancelled.",
                5 => "Access was denied by Windows security permissions.",
                112 => "There is not enough free disk space to finish. Free space on your drive and try again.",
                32 => "The file is locked because another program is using it. Close other media apps and try again.",
                _ => $"Windows system error ({w32.NativeErrorCode}): {CleanMessage(w32.Message)}"
            };
        }

        if (root is COMException com)
        {
            var hr = (uint)com.HResult;
            if (hr is 0xC00D36B4 or 0xC00D5212 or 0x88890008)
            {
                return "Windows could not decode this video format. Try a video exported as standard H.264 MP4.";
            }
            if (hr is 0x80070005)
            {
                return "Access denied by Windows security. Check your permissions and try again.";
            }
            if (hr is 0x80070070)
            {
                return "There is not enough disk space available. Free up drive space and retry.";
            }
            return $"Media decoding failed ({com.HResult:X8}). Try another video format or an existing GIF.";
        }

        if (root is IOException io)
        {
            var hr = io.HResult & 0xFFFF;
            if (hr == 112)
            {
                return "There is not enough free disk space. Free up disk space and try again.";
            }
            if (hr == 32)
            {
                return "The file is being used by another application. Close any media players and try again.";
            }
            return CleanMessage(io.Message);
        }

        if (root is InvalidDataException)
        {
            return "The selected media contains invalid or corrupt frame data. Try another video or GIF.";
        }

        if (root is OperationCanceledException)
        {
            return "The operation was cancelled.";
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return CleanMessage(root.Message);
    }

    private static string CleanMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "The operation could not be completed.";
        }

        // Strip out type name prefixes like "System.IO.IOException: "
        var colonIdx = raw.IndexOf(':');
        if (colonIdx > 0 && colonIdx < 40 && raw.Substring(0, colonIdx).Contains('.'))
        {
            raw = raw.Substring(colonIdx + 1).Trim();
        }

        return raw;
    }
}
