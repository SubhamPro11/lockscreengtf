using System.Text;

public static class Logger
{
    private static readonly object lockObj = new();
    private static string? logFilePath;

    public static string GetLogPath()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LockscreenGif");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }

    private static string GetLogFilePath()
    {
        if (logFilePath == null)
        {
            var fileName = $"app_{DateTime.Now:yyyy-MM-dd}.log";
            logFilePath = Path.Combine(GetLogPath(), fileName);
        }
        return logFilePath;
    }

    public static void CleanupOldLogFiles()
    {
        var oldFiles = new DirectoryInfo(GetLogPath()).GetFiles("app_*.log").Where(f => f.CreationTime < DateTime.Now.AddDays(-7)).ToList();

        foreach (var file in oldFiles)
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex)
            {
                Error("Failed to delete log file", ex);
            }
        }
    }

    public static void Info(string? message)
    {
        if (message != null)
        {
            Log("INFO", message);
        }
    }

    public static void Warn(string message)
    {
        Log("WARN", message);
    }

    public static void Error(string? message)
    {
        if (message != null)
        {
            Error(message, null);
        }
    }

    public static void Error(string message, Exception? ex = null)
    {
        var logMessage = new StringBuilder(message);
        if (ex != null)
        {
            logMessage.AppendLine(); // Ensure the exception starts on a new line
            logMessage.AppendLine($"Exception: {ex.GetType().FullName} (HRESULT 0x{ex.HResult:X8}): {ex.Message}");
            logMessage.AppendLine($"StackTrace: {ex.StackTrace}");
        }

        Log("ERROR", logMessage.ToString());
    }

    public static void Fatal(string message, Exception? ex = null)
    {
        var logMessage = new StringBuilder(message);
        if (ex != null)
        {
            logMessage.AppendLine(); // Ensure the exception starts on a new line
            logMessage.AppendLine($"Exception: {ex.GetType().FullName} (HRESULT 0x{ex.HResult:X8}): {ex.Message}");
            logMessage.AppendLine($"StackTrace: {ex.StackTrace}");
        }
        Log("FATAL", logMessage.ToString());
    }

    private const long MaxLogFileSize = 2 * 1024 * 1024; // 2 MB
    private const int MaxRotatedFiles = 5;

    private static void RotateLogsIfNeeded(string currentFilePath)
    {
        try
        {
            var fileInfo = new FileInfo(currentFilePath);
            if (!fileInfo.Exists || fileInfo.Length < MaxLogFileSize)
            {
                return;
            }

            // Remove oldest archive if it exists
            var oldest = $"{currentFilePath}.{MaxRotatedFiles}";
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            // Shift archives down: e.g. .4 -> .5, .3 -> .4
            for (var i = MaxRotatedFiles - 1; i >= 1; i--)
            {
                var src = $"{currentFilePath}.{i}";
                var dst = $"{currentFilePath}.{i + 1}";
                if (File.Exists(src))
                {
                    File.Move(src, dst, true);
                }
            }

            // Rotate current file to .1
            var firstArchive = $"{currentFilePath}.1";
            File.Move(currentFilePath, firstArchive, true);
        }
        catch
        {
            // Logging rotation failure should never crash the caller
        }
    }

    private static void Log(string level, string message)
    {
        try
        {
            lock (lockObj)
            {
                var filePath = GetLogFilePath();
                RotateLogsIfNeeded(filePath);
                using var sw = new StreamWriter(filePath, true, Encoding.UTF8);
                sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}");
            }
        }
        catch { }
    }
}
