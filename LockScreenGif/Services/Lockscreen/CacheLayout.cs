namespace LockscreenGif.Services.Lockscreen;

internal sealed class CacheLayout(string directory, CachePermissions permissions)
{
    public const string MainImageName = "LockScreen.jpg";
    public const string VariantSuffix = "_notdimmed.jpg";

    public async Task<string[]> FindFoldersAsync(ApplyProgress progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return ReadFolders();
        }
        catch (Exception ex) when (CachePermissions.IsAccessDenied(ex))
        {
            await permissions.GrantAsync(directory, false, progress, cancellationToken);
            return ReadFolders();
        }
    }

    private string[] ReadFolders()
    {
        permissions.ValidatePath(directory);
        return Directory
            .GetDirectories(directory)
            .Where(path => Path.GetFileName(path).StartsWith("LockScreen", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<string[]> FindVariantsAsync(string folder, ApplyProgress progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return ReadVariants(folder);
        }
        catch (Exception ex) when (CachePermissions.IsAccessDenied(ex))
        {
            await permissions.GrantAsync(folder, false, progress, cancellationToken);
            return ReadVariants(folder);
        }
    }

    private string[] ReadVariants(string folder)
    {
        permissions.ValidatePath(folder);
        return Directory.GetFiles(folder, "*" + VariantSuffix);
    }

    public async Task<List<string>> FindDestinationsAsync(ApplyProgress progress, CancellationToken cancellationToken)
    {
        var folders = await FindFoldersAsync(progress, cancellationToken);
        var resolutions = Array.Empty<string>();
        try
        {
            resolutions = DisplayService.GetDisplayResolutions().ToArray();
        }
        catch (Exception ex)
        {
            progress.Report("Discovery", $"Display enumeration unavailable: {ApplyProgress.Describe(ex)}", severity: "Warning");
        }

        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var variants = await FindVariantsAsync(folder, progress, cancellationToken);
            destinations.Add(Path.Combine(folder, MainImageName));
            foreach (var resolution in resolutions)
            {
                destinations.Add(Path.Combine(folder, $"LockScreen___{resolution}{VariantSuffix}"));
            }

            foreach (var existing in variants)
            {
                destinations.Add(existing);
            }
        }
        progress.Report("Discovery", $"Found {folders.Length} cache folders and {destinations.Count} destinations.");
        return destinations.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task WaitForSettlingAsync(ApplyProgress progress, CancellationToken cancellationToken)
    {
        string? previous = null;
        var stableSamples = 0;
        // This is a bounded cache quiet period, not a promise that Windows has finished.
        for (var sample = 0; sample < 10; sample++)
        {
            await Task.Delay(500, cancellationToken);
            try
            {
                var entries = new List<string>();
                string[] folders;
                try
                {
                    folders = await FindFoldersAsync(progress, cancellationToken);
                }
                catch (DirectoryNotFoundException)
                {
                    folders = [];
                }

                foreach (var folder in folders)
                {
                    permissions.ValidatePath(folder);
                    string[] files;
                    try
                    {
                        files = Directory.GetFiles(folder);
                    }
                    catch (Exception ex) when (CachePermissions.IsAccessDenied(ex))
                    {
                        await permissions.GrantAsync(folder, false, progress, cancellationToken);
                        files = Directory.GetFiles(folder);
                    }

                    foreach (var path in files)
                    {
                        permissions.ValidatePath(path);
                        try
                        {
                            var info = new FileInfo(path);
                            if (info.Exists)
                            {
                                entries.Add($"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            entries.Add($"{path}|locked");
                        }
                    }
                }

                var current = string.Join("\n", entries.Order(StringComparer.OrdinalIgnoreCase));
                stableSamples = current == previous ? stableSamples + 1 : 0;
                previous = current;
                if (stableSamples >= 3)
                {
                    progress.Report("WindowsApi", "The cache inventory was unchanged across three samples.");
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                progress.Report("WindowsApi", $"Cache settling could not be observed: {ApplyProgress.Describe(ex)}", severity: "Warning");
                return;
            }
        }

        progress.Report("WindowsApi", "The cache did not settle within five seconds; rediscovering destinations.", severity: "Warning");
    }
}
